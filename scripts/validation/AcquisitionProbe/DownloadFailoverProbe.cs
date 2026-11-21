using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

internal static class DownloadFailoverProbe
{
    private const string Hash = "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951";

    internal static void Verify(string root)
    {
        VerifyOrdering(root);
        VerifyPreflight(root);
        VerifyExplicitRouting(root, false);
        VerifyExplicitRouting(root, true);
        VerifyReleaseFailures(root);
        VerifyUncertainRestart(root);
        VerifyCrashMarker(root);
        VerifyPostAddFailure(root);
        VerifyReconciliation(root, "exact");
        VerifyReconciliation(root, "empty");
        VerifyReconciliation(root, "duplicate");
        VerifyReconciliation(root, "other_client");
        VerifyReconciliation(root, "title_only");
        VerifyReconciliation(root, "unavailable");
    }

    private static void VerifyOrdering(string root)
    {
        var scenario = new AttemptScenario(root, "ordering");
        scenario.First.Definition.Priority = 1;
        scenario.Second.Definition.Priority = 2;
        var third = scenario.AddClient(3, 1);
        var provider = scenario.Provider;
        Program.Check("published_failover_candidates_preserve_priority", provider.GetDownloadClients(DownloadProtocol.Usenet)
            .Select(client => client.Definition.Id).SequenceEqual(new[] { 1, 3, 2 }));
        Program.Check("published_candidate_lookup_does_not_advance_round_robin", provider.GetDownloadClient(DownloadProtocol.Usenet).Definition.Id == 1 &&
            provider.GetDownloadClient(DownloadProtocol.Usenet).Definition.Id == 1);
        provider.ReportSuccessfulDownloadClient(DownloadProtocol.Usenet, 1);
        Program.Check("published_success_advances_round_robin_within_priority", provider.GetDownloadClients(DownloadProtocol.Usenet)
            .Select(client => client.Definition.Id).SequenceEqual(new[] { 3, 1, 2 }));
        third.Definition.Tags.Add(99);
        Program.Check("published_failover_candidates_cannot_escape_author_tags", provider.GetDownloadClients(DownloadProtocol.Usenet)
            .Select(client => client.Definition.Id).SequenceEqual(new[] { 1, 2 }));
    }

    private static void VerifyPreflight(string root)
    {
        var scenario = new AttemptScenario(root, "preflight");
        scenario.First.StatusFailure = new DownloadClientUnavailableException("Owned client offline before add");
        scenario.Run();
        Program.Check("published_preflight_unavailable_falls_back_without_first_add", scenario.First.Adds == 0 && scenario.Second.Adds == 1);
        Program.Check("published_safe_failover_records_only_one_successful_grab", scenario.Grabs.Count == 1 && scenario.Grabs[0].DownloadClientId == 2 &&
            scenario.Store.DownloadRows("owned-nzb-2").Count(row => row.EventType == DownloadHistoryEventType.DownloadGrabbed) == 1);
        Program.Check("published_safe_failover_has_durable_guard_before_client_add", scenario.Second.MarkerBeforeAdd);
        Program.Check("published_client_only_failure_does_not_fail_indexer", scenario.ClientFailures.SequenceEqual(new[] { 1 }) && scenario.IndexerFailures == 0);
        Program.Check("published_successful_submission_clears_only_pending_marker", scenario.Guard.GetPending().Count == 0 && scenario.Store.GetSubmissionAttempts().Count == 0);
    }

    private static void VerifyExplicitRouting(string root, bool indexer)
    {
        var prefix = indexer ? "indexer_routed" : "user_selected";
        var scenario = new AttemptScenario(root, prefix);
        scenario.First.StatusFailure = new DownloadClientUnavailableException("Owned selected client offline");
        if (indexer)
        {
            scenario.Book.Release.IndexerId = 7;
            scenario.IndexerClientId = 1;
        }
        var stopped = Throws<DownloadClientUnavailableException>(() => scenario.Run(indexer ? null : 1));
        Program.Check("published_" + prefix + "_client_never_falls_back", stopped && scenario.First.Adds == 0 && scenario.Second.Adds == 0 && scenario.Second.StatusCalls == 0);
        Program.Check("published_" + prefix + "_failure_creates_no_grab_or_uncertain_hold", scenario.Grabs.Count == 0 && scenario.Guard.GetPending().Count == 0);
    }

    private static void VerifyReleaseFailures(string root)
    {
        foreach (var kind in new[] { "unavailable", "blocked", "duplicate", "fetch" })
        {
            var scenario = new AttemptScenario(root, "release-" + kind);
            scenario.First.AddFailure = kind switch
            {
                "unavailable" => new ReleaseUnavailableException(scenario.Book.Release, "Owned release gone"),
                "blocked" => new ReleaseBlockedException(scenario.Book.Release, "Owned blocked release"),
                "duplicate" => new DownloadClientRejectedReleaseException(scenario.Book.Release, "Owned duplicate"),
                _ => new ReleaseDownloadException(scenario.Book.Release, "Owned fetch failure")
            };
            var stopped = Throws<ReleaseDownloadException>(() => scenario.Run());
            Program.Check("published_" + kind + "_release_stops_client_failover", stopped && scenario.First.Adds == 1 && scenario.Second.Adds == 0 && scenario.Second.StatusCalls == 0 && scenario.Grabs.Count == 0);
            Program.Check("published_" + kind + "_release_rejection_does_not_leave_false_uncertain_hold", scenario.Guard.GetPending().Count == 0);
            Program.Check("published_" + kind + "_release_has_correct_indexer_failure_accounting", scenario.ClientFailures.Count == 0 && scenario.IndexerFailures == (kind == "fetch" ? 1 : 0));
        }
    }

    private static void VerifyUncertainRestart(string root)
    {
        var scenario = new AttemptScenario(root, "uncertain-restart");
        scenario.First.AddFailure = new DownloadClientUnavailableException("Owned response lost after add");
        Program.Check("published_add_unavailable_is_uncertain_instead_of_failover", Throws<DownloadClientSubmissionUncertainException>(() => scenario.Run()) &&
            scenario.First.Adds == 1 && scenario.Second.Adds == 0 && scenario.Grabs.Count == 0);
        var holds = scenario.Guard.GetPending();
        Program.Check("published_uncertain_add_has_client_scoped_durable_hold", holds.Count == 1 && holds[0].DownloadClientId == 1 &&
            holds[0].BookIds.SequenceEqual(new[] { 12, 99 }) && holds[0].CanAcknowledge && scenario.IndexerFailures == 0);
        var attempt = holds.Single().AttemptId;
        scenario.Restart();
        Program.Check("published_uncertain_hold_survives_sqlite_serialization_and_service_restart", scenario.Guard.GetPending().Single().AttemptId == attempt &&
            Throws<DownloadClientSubmissionUncertainException>(() => scenario.Run()) && scenario.First.Adds == 1 && scenario.Second.Adds == 0);
        Program.Check("published_uncertain_hold_cannot_be_acknowledged_for_another_client", !scenario.Guard.Acknowledge(attempt, 2) &&
            !scenario.Guard.Acknowledge("wrong-attempt", 1) && scenario.Guard.GetPending().Count == 1);
        Program.Check("published_operator_acknowledgement_resolves_exact_hold", scenario.Guard.Acknowledge(attempt, 1) && scenario.Guard.GetPending().Count == 0);
        scenario.Restart();
        scenario.First.AddFailure = null;
        scenario.Run();
        Program.Check("published_acknowledged_hold_allows_future_submission_and_retains_neutral_audit", scenario.First.Adds == 2 && scenario.Grabs.Count == 1 &&
            scenario.Store.GetSubmissionAttempts().Count == 1 && scenario.Guard.GetPending().Count == 0);
    }

    private static void VerifyCrashMarker(string root)
    {
        var scenario = new AttemptScenario(root, "crash-marker");
        var attempt = scenario.Guard.Begin(scenario.Book, scenario.First.Client);
        Program.Check("published_inflight_submission_hold_cannot_be_acknowledged", !scenario.Guard.GetPending().Single().CanAcknowledge && !scenario.Guard.Acknowledge(attempt, 1));
        scenario.Restart();
        Program.Check("published_crashed_preadd_marker_stops_new_submissions_after_restart", scenario.Guard.GetPending().Single().CanAcknowledge &&
            Throws<DownloadClientSubmissionUncertainException>(() => scenario.Run()) && scenario.First.Adds == 0 && scenario.Second.Adds == 0);
    }

    private static void VerifyPostAddFailure(string root)
    {
        var scenario = new AttemptScenario(root, "post-add-failure");
        scenario.FailStatusSuccess = true;
        Program.Check("published_post_add_recording_failure_stops_fallback", Throws<DownloadClientSubmissionUncertainException>(() => scenario.Run()) &&
            scenario.First.Adds == 1 && scenario.Second.Adds == 0 && scenario.Grabs.Count == 0);
        var hold = scenario.Guard.GetPending().Single();
        Program.Check("published_post_add_recording_failure_releases_hold_for_review_with_confirmed_id", hold.CanAcknowledge && hold.ExpectedDownloadId == "owned-nzb-1");
    }

    private static void VerifyReconciliation(string root, string state)
    {
        var scenario = new AttemptScenario(root, "reconcile-" + state, DownloadProtocol.Torrent);
        scenario.First.AddFailure = new DownloadClientSubmissionUncertainException("Owned add response lost", new TimeoutException(), Hash);
        scenario.First.Items = state switch
        {
            "exact" => new[] { new DownloadClientItem { DownloadId = Hash.ToLowerInvariant(), Title = "Unrelated display title" } },
            "duplicate" => new[] { new DownloadClientItem { DownloadId = Hash }, new DownloadClientItem { DownloadId = Hash } },
            "other_client" => new[] { new DownloadClientItem { DownloadId = Hash, DownloadClientInfo = new DownloadClientItemClientInfo { Id = 2 } } },
            "title_only" => new[] { new DownloadClientItem { DownloadId = new string('0', 40), Title = scenario.Book.Release.Title } },
            _ => Array.Empty<DownloadClientItem>()
        };
        if (state == "unavailable") scenario.First.PollFailure = new DownloadClientUnavailableException("Owned reconciliation unavailable");
        var uncertain = Throws<DownloadClientSubmissionUncertainException>(() => scenario.Run());
        Program.Check("published_" + state + "_reconciliation_never_adds_on_another_client", scenario.First.Adds == 1 && scenario.First.Polls == 1 && scenario.Second.Adds == 0 && scenario.Second.Polls == 0);
        if (state == "exact")
        {
            Program.Check("published_strong_same_client_hash_confirms_one_successful_grab", !uncertain && scenario.Grabs.Count == 1 &&
                scenario.Grabs[0].DownloadId == Hash.ToLowerInvariant() && scenario.Grabs[0].DownloadClientId == 1 && scenario.Guard.GetPending().Count == 0 &&
                scenario.Store.DownloadRows(Hash.ToLowerInvariant()).Count(row => row.EventType == DownloadHistoryEventType.DownloadGrabbed) == 1);
        }
        else
        {
            Program.Check("published_" + state + "_reconciliation_requires_operator_review", uncertain && scenario.Grabs.Count == 0 &&
                scenario.Guard.GetPending().Single().ExpectedDownloadId == Hash);
        }
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    internal sealed class ClientState
    {
        public DownloadClientDefinition Definition;
        public IDownloadClient Client;
        public DownloadClientInfo Status = new();
        public Exception StatusFailure;
        public Exception AddFailure;
        public Exception PollFailure;
        public DownloadClientItem[] Items = Array.Empty<DownloadClientItem>();
        public int Adds;
        public int StatusCalls;
        public int Polls;
        public bool MarkerBeforeAdd;
    }

    internal sealed class AttemptScenario
    {
        public readonly Program.Store Store;
        public readonly RemoteBook Book;
        public readonly List<BookGrabbedEvent> Grabs = new();
        public readonly List<int> ClientFailures = new();
        public readonly DownloadClientProvider Provider;
        public readonly ClientState First;
        public readonly ClientState Second;
        public DownloadSubmissionGuard Guard;
        public int IndexerFailures;
        public int IndexerClientId;
        public bool FailStatusSuccess;
        private readonly List<ClientState> _clients = new();
        private readonly Dictionary<Type, object> _services = new();
        private readonly IDownloadHistoryRepository _repository;
        private DownloadService _service;

        public AttemptScenario(string root, string name, DownloadProtocol protocol = DownloadProtocol.Usenet, IDownloadGrabSpaceService grabSpace = null)
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "d09-" + name)).FullName;
            var author = new Author { Id = 5, Name = "Owned failover author", Tags = new HashSet<int>() };
            Store = new Program.Store(Path.Combine(folder, "owned-history.sqlite"), author);
            Book = new RemoteBook
            {
                Author = author,
                Books = new List<Book> { new() { Id = 12, Author = author }, new() { Id = 99, Author = author } },
                Release = new ReleaseInfo { Title = "Owned release", DownloadProtocol = protocol, DownloadUrl = "http://127.0.0.1/owned-fake-release" }
            };
            _repository = Program.Fake<IDownloadHistoryRepository>((method, arguments) => method.Name switch
            {
                "Insert" => Store.Insert((DownloadHistory)arguments[0]),
                "Update" => Store.Update((DownloadHistory)arguments[0]),
                "Delete" => DeleteMarker((DownloadHistory)arguments[0]),
                "GetSubmissionAttempts" => Store.GetSubmissionAttempts(),
                "FindByDownloadId" => Store.DownloadRows((string)arguments[0]),
                _ => Program.Unexpected(method)
            });
            var history = new DownloadHistoryService(_repository, Program.Fake<IHistoryService>((method, _) => Program.Unexpected(method)));
            var status = Program.Fake<IDownloadClientStatusService>((method, arguments) => method.Name switch
            {
                "GetBlockedProviders" => new List<DownloadClientStatus>(),
                "RecordSuccess" => RecordClientSuccess(),
                "RecordFailure" => RecordClientFailure((int)arguments[0]),
                _ => Program.Unexpected(method)
            });
            var indexers = Program.Fake<IIndexerFactory>((method, _) => method.Name switch
            {
                "Find" => new IndexerDefinition { Id = 7, Name = "Owned indexer", DownloadClientId = IndexerClientId },
                "Get" => new IndexerDefinition { Id = 7, Name = "Owned indexer", DownloadClientId = IndexerClientId },
                "GetInstance" => null,
                _ => Program.Unexpected(method)
            });
            var factory = Program.Fake<IDownloadClientFactory>((method, _) => method.Name == "GetAvailableProviders" ? _clients.Select(client => client.Client).ToList() : Program.Unexpected(method));
            var logger = LogManager.GetLogger("OwnedFailoverProbe");
            Provider = new DownloadClientProvider(status, factory, indexers, new CacheManager(), logger);
            First = AddClient(1, 1);
            Second = AddClient(2, 2);
            _services[typeof(IProvideDownloadClient)] = Provider;
            _services[typeof(IDownloadClientStatusService)] = status;
            _services[typeof(IIndexerFactory)] = indexers;
            _services[typeof(IIndexerStatusService)] = Program.Fake<IIndexerStatusService>((method, _) => method.Name switch
            {
                "RecordSuccess" => null,
                "RecordFailure" => RecordIndexerFailure(),
                _ => Program.Unexpected(method)
            });
            _services[typeof(IRateLimitService)] = Program.Fake<IRateLimitService>((method, _) => method.Name == "WaitAndPulseAsync" ? Task.CompletedTask : Program.Unexpected(method));
            _services[typeof(ISeedConfigProvider)] = Program.Fake<ISeedConfigProvider>((method, _) => method.Name == "GetSeedConfiguration" ? new TorrentSeedConfiguration() : Program.Unexpected(method));
            _services[typeof(IDownloadGrabSpaceService)] = grabSpace ?? Program.Fake<IDownloadGrabSpaceService>((method, _) => method.Name == "Check" ? new GrabSpaceAssessment(Decision.Accept()) : Program.Unexpected(method));
            _services[typeof(IEventAggregator)] = Program.Fake<IEventAggregator>((method, arguments) =>
            {
                if (method.Name != "PublishEvent" || arguments[0] is not BookGrabbedEvent grabbed) return Program.Unexpected(method);
                Grabs.Add(grabbed);
                history.Handle(grabbed);
                return null;
            });
            _services[typeof(Logger)] = logger;
            Restart();
        }

        public ClientState AddClient(int id, int priority)
        {
            var state = new ClientState
            {
                Definition = new DownloadClientDefinition { Id = id, Name = "Owned client " + id, Protocol = Book.Release.DownloadProtocol, Priority = priority, Tags = new HashSet<int>() }
            };
            state.Client = Program.Fake<IDownloadClient>((method, _) => method.Name switch
            {
                "get_Definition" => state.Definition,
                "get_Name" => state.Definition.Name,
                "get_Protocol" => state.Definition.Protocol,
                "GetStatus" => Preflight(state),
                "Download" => Add(state),
                "GetItems" => Items(state),
                _ => Program.Unexpected(method)
            });
            _clients.Add(state);
            return state;
        }

        public void Restart()
        {
            Guard = new DownloadSubmissionGuard(_repository);
            _services[typeof(IDownloadSubmissionGuard)] = Guard;
            _service = Program.Construct<DownloadService>(_services);
        }

        public void Run(int? clientId = null) => _service.DownloadReport(Book, clientId).GetAwaiter().GetResult();
        public void Run(int clientId, DownloadSpaceBudget budget) => _service.DownloadReport(Book, clientId, budget).GetAwaiter().GetResult();
        private object DeleteMarker(DownloadHistory marker) { Store.Delete(marker); return null; }
        private object RecordClientSuccess()
        {
            if (FailStatusSuccess) throw new IOException("Owned status persistence failure after accepted add");
            return null;
        }
        private object RecordClientFailure(int clientId) { ClientFailures.Add(clientId); return null; }
        private object RecordIndexerFailure() { IndexerFailures++; return null; }
        private static DownloadClientInfo Preflight(ClientState client)
        {
            client.StatusCalls++;
            if (client.StatusFailure != null) throw client.StatusFailure;
            return client.Status;
        }
        private Task<string> Add(ClientState client)
        {
            client.Adds++;
            client.MarkerBeforeAdd = Guard.GetPending().Any(hold => hold.DownloadClientId == client.Definition.Id && !hold.CanAcknowledge);
            return client.AddFailure != null ? Task.FromException<string>(client.AddFailure) : Task.FromResult("owned-nzb-" + client.Definition.Id);
        }
        private static DownloadClientItem[] Items(ClientState client)
        {
            client.Polls++;
            if (client.PollFailure != null) throw client.PollFailure;
            return client.Items;
        }
    }
}
