using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.RootFolders;

internal static class GrabSpaceProbe
{
    private const long MiB = 1024L * 1024;

    internal static void Verify(string root)
    {
        var fixture = Directory.CreateDirectory(Path.Combine(root, "grab-space")).FullName;
        var library = Directory.CreateDirectory(Path.Combine(fixture, "library")).FullName;
        var destination = Path.Combine(library, "owned-author");
        var output = Directory.CreateDirectory(Path.Combine(fixture, "client-output")).FullName;
        var otherMountPath = Directory.CreateDirectory(Path.Combine(fixture, "other-client-output")).FullName;
        var enabled = false;
        var bytesAvailable = 500 * MiB;
        var diskCalls = 0;
        var currentAuthor = new Author { Id = 5, Path = destination };

        IMount Mount(string mountPath) => Program.Fake<IMount>((method, _) => method.Name switch
        {
            "get_RootDirectory" => mountPath,
            "get_IsReady" => true,
            "get_MountOptions" => null,
            "get_AvailableFreeSpace" => bytesAvailable,
            _ => Program.Unexpected(method)
        });

        var sharedMount = Mount(fixture);
        var otherMount = Mount(otherMountPath);
        var disk = Program.Fake<IDiskProvider>((method, arguments) =>
        {
            diskCalls++;
            var path = (string)arguments[0];
            return method.Name switch
            {
                "FolderExists" => Directory.Exists(path),
                "GetFileAttributes" => File.GetAttributes(path),
                "GetMount" => path == otherMountPath ? otherMount : sharedMount,
                _ => Program.Unexpected(method)
            };
        });
        var config = Program.Fake<IConfigService>((method, _) => method.Name switch
        {
            "get_CheckFreeSpaceWhenGrabbing" => enabled,
            "get_MinimumFreeSpaceWhenImporting" => 100,
            _ => Program.Unexpected(method)
        });
        var roots = Program.Fake<IRootFolderService>((method, arguments) => method.Name == "GetBestRootFolder"
            ? (string)arguments[0] == destination ? new RootFolder { Path = library } : null
            : Program.Unexpected(method));
        var authors = Program.Fake<IAuthorService>((method, _) => method.Name == "GetAuthor" ? currentAuthor : Program.Unexpected(method));
        var service = new DownloadGrabSpaceService(config, authors, roots, disk, LogManager.GetLogger("OwnedGrabSpaceProbe"));
        var specification = new GrabFreeSpaceSpecification(service);
        var scenario = new DownloadFailoverProbe.AttemptScenario(fixture, "grab-space", grabSpace: service);
        var remote = scenario.Book;
        remote.Author.Path = destination;

        Program.Check("published_disabled_grab_space_policy_keeps_unknown_size_and_skips_disk", specification.IsSatisfiedBy(remote, null).Accepted && diskCalls == 0);
        enabled = true;
        Program.Check("published_enabled_grab_space_rejects_unknown_size_with_stable_code", specification.IsSatisfiedBy(remote, null).Code == RejectionCode.GrabFreeSpaceUnverified);
        remote.Release.Size = 400 * MiB;
        Program.Check("published_grab_space_preview_accepts_exact_staged_size_plus_reserve", specification.IsSatisfiedBy(remote, null).Accepted);

        scenario.First.Status.IsLocalhost = true;
        scenario.First.Status.OutputRootFolders.Add(new OsPath(output));
        Program.Check("published_direct_grab_same_mount_counts_source_and_staged_copy_before_add",
            Rejected(() => scenario.Run(1), RejectionCode.MinimumFreeSpace) && scenario.First.Adds == 0 && scenario.Guard.GetPending().Count == 0);
        scenario.First.Status.OutputRootFolders[0] = new OsPath(otherMountPath);
        Program.Check("published_other_mount_add_counts_only_the_library_staging_bytes", service.Check(remote, null, scenario.First.Status).Bytes == remote.Release.Size);
        scenario.First.Status.OutputRootFolders[0] = new OsPath(output);
        bytesAvailable = 900 * MiB;
        var budget = new DownloadSpaceBudget();
        scenario.Run(1, budget);
        Program.Check("published_grab_space_credits_no_existing_bytes_and_charges_actual_same_mount_submit", scenario.First.Adds == 1 && budget.GetSubmittedBytes(fixture) == 800 * MiB);
        Program.Check("published_batch_grab_space_blocks_second_submission_on_shared_mount",
            Rejected(() => scenario.Run(1, budget), RejectionCode.MinimumFreeSpace) && scenario.First.Adds == 1 && scenario.Guard.GetPending().Count == 0);
        Program.Check("published_grab_space_budget_does_not_become_a_global_reservation", service.Check(remote, new DownloadSpaceBudget(), scenario.First.Status).Decision.Accepted);

        currentAuthor = new Author { Id = 5, Path = Path.Combine(fixture, "missing-library", "owned-author") };
        Program.Check("published_grab_space_revalidates_current_author_instead_of_cached_release_path",
            Rejected(() => scenario.Run(1), RejectionCode.GrabFreeSpaceUnverified) && scenario.First.Adds == 1 && scenario.Grabs.Count == 1);
    }

    private static bool Rejected(Action submit, string code)
    {
        try
        {
            submit();
            return false;
        }
        catch (DownloadGrabSpaceException ex)
        {
            return ex.RejectionCode == code;
        }
    }
}
