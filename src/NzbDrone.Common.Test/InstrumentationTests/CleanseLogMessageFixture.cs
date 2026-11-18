using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Test.InstrumentationTests
{
    [TestFixture]
    public class CleanseLogMessageFixture
    {
        // Indexer Urls
        [TestCase(@"https://iptorrents.com/torrents/rss?u=mySecret;tp=mySecret;l5;download")]
        [TestCase(@"http://rss.torrentleech.org/mySecret")]
        [TestCase(@"https://rss24h.torrentleech.org/mySecret")]
        [TestCase(@"http://rss.torrentleech.org/rss/download/12345/01233210/file.name-RLSGRP.torrent")]
        [TestCase(@"https://www.torrentleech.org/rss/download/12345/01233210/file.name-RLSGRP.torrent")]
        [TestCase(@"http://www.bitmetv.org/rss.php?uid=mySecret&passkey=mySecret")]
        [TestCase(@"https://rss.omgwtfnzbs.org/rss-search.php?catid=19,20&user=Readarr&api=mySecret&eng=1")]
        [TestCase(@"https://dognzb.cr/fetch/2b51db35e1912ffc138825a12b9933d2/2b51db35e1910123321025a12b9933d2")]
        [TestCase(@"https://baconbits.org/feeds.php?feed=torrents_tv&user=12345&auth=2b51db35e1910123321025a12b9933d2&passkey=mySecret&authkey=2b51db35e1910123321025a12b9933d2")]
        [TestCase(@"http://127.0.0.1:9117/dl/indexername?jackett_apikey=flwjiefewklfjacketmySecretsdfldskjfsdlk&path=we0re9f0sdfbase64sfdkfjsdlfjk&file=The+Torrent+File+Name.torrent")]
        [TestCase(@"http://nzb.su/getnzb/2b51db35e1912ffc138825a12b9933d2.nzb&i=37292&r=2b51db35e1910123321025a12b9933d2")]
        [TestCase(@"https://b-hd.me/torrent/download/auto.343756.is1t1pl127p1sfwur8h4kgyhg1wcsn05")]
        [TestCase(@"https://b-hd.me/torrent/download/a-slug-in-the-url.343756.is1t1pl127p1sfwur8h4kgyhg1wcsn05")]

        // NzbGet
        [TestCase(@"{ ""Name"" : ""ControlUsername"", ""Value"" : ""mySecret"" }, { ""Name"" : ""ControlPassword"", ""Value"" : ""mySecret"" }, ")]
        [TestCase(@"{ ""Name"" : ""Server1.Username"", ""Value"" : ""mySecret"" }, { ""Name"" : ""Server1.Password"", ""Value"" : ""mySecret"" }, ")]

        // Sabnzbd
        [TestCase(@"http://127.0.0.1:1234/api/call?vv=1&apikey=mySecret")]
        [TestCase(@"http://127.0.0.1:1234/api/call?vv=1&ma_username=mySecret&ma_password=mySecret")]
        [TestCase(@"""config"":{""newzbin"":{""username"":""mySecret"",""password"":""mySecret""}")]
        [TestCase(@"""nzbxxx"":{""username"":""mySecret"",""apikey"":""mySecret""}")]
        [TestCase(@"""growl"":{""growl_password"":""mySecret"",""growl_server"":""""}")]
        [TestCase(@"""nzbmatrix"":{""username"":""mySecret"",""apikey"":""mySecret""}")]
        [TestCase(@"""misc"":{""username"":""mySecret"",""api_key"":""mySecret"",""password"":""mySecret"",""nzb_key"":""mySecret""}")]
        [TestCase(@"""servers"":[{""username"":""mySecret"",""password"":""mySecret""}]")]
        [TestCase(@"""misc"":{""email_account"":""mySecret"",""email_to"":[],""email_from"":"""",""email_pwd"":""mySecret""}")]

        // uTorrent
        [TestCase(@"http://localhost:9091/gui/?token=wThmph5l0ZXfH-a6WOA4lqiLvyjCP0FpMrMeXmySecret_VXBO11HoKL751MAAAAA&list=1")]
        [TestCase(@",[""boss_key"",0,""mySecret"",{""access"":""Y""}],[""boss_key_salt"",0,""mySecret"",{""access"":""W""}]")]
        [TestCase(@",[""webui.username"",2,""mySecret"",{""access"":""Y""}],[""webui.password"",2,""mySecret"",{""access"":""Y""}]")]
        [TestCase(@",[""webui.uconnect_username"",2,""mySecret"",{""access"":""Y""}],[""webui.uconnect_password"",2,""mySecret"",{""access"":""Y""}]")]
        [TestCase(@",[""proxy.proxy"",2,""mySecret"",{""access"":""Y""}]")]
        [TestCase(@",[""proxy.username"",2,""mySecret"",{""access"":""Y""}],[""proxy.password"",2,""mySecret"",{""access"":""Y""}]")]

        // Deluge
        [TestCase(@",{""download_location"": ""C:\Users\\mySecret mySecret\\Downloads""}")]
        [TestCase(@",{""download_location"": ""/home/mySecret/Downloads""}")]
        [TestCase(@",{""download_location"": ""/Users/mySecret/Downloads""}")]
        [TestCase(@"auth.login(""mySecret"")")]

        // Download Station
        [TestCase(@"webapi/entry.cgi?api=(removed)&version=2&method=login&account=01233210&passwd=mySecret&format=sid&session=DownloadStation")]

        // BroadcastheNet
        [TestCase(@"method: ""getTorrents"", ""params"": [ ""mySecret"",")]
        [TestCase(@"getTorrents(""mySecret"", [asdfasdf], 100, 0)")]
        [TestCase(@"""DownloadURL"":""https:\/\/broadcasthe.net\/torrents.php?action=download&id=123&authkey=mySecret&torrent_pass=mySecret""")]

        // Internal
        [TestCase(@"OutputPath=/home/mySecret/Downloads")]
        [TestCase(@"OutputPath=/Users/mySecret/Downloads")]
        [TestCase("Hardlinking episode file: /home/mySecret/Downloads to /media/abc.mkv")]
        [TestCase("Hardlinking episode file: /Users/mySecret/Downloads to /media/abc.mkv")]
        [TestCase("Hardlink '/home/mySecret/Downloads/abs.mkv' to '/media/abc.mkv' failed.")]
        [TestCase("Hardlink '/Users/mySecret/Downloads/abs.mkv' to '/media/abc.mkv' failed.")]
        [TestCase("https://notifiarr.com/notifier.php: api=1234530f-422f-4aac-b6b3-01233210aaaa&radarr_health_issue_message=Download")]
        [TestCase("/readarr/signalr/messages/negotiate?access_token=1234530f422f4aacb6b301233210aaaa&negotiateVersion=1")]
        [TestCase(@"[Info] MigrationController: *** Migrating Database=readarr-main;Host=postgres14;Username=mySecret;Password=mySecret;Port=5432;Enlist=False ***")]
        [TestCase(@"[Info] MigrationController: *** Migrating Database=readarr-main;Host=postgres14;Username=mySecret;Password=mySecret;Port=5432;token=mySecret;Enlist=False&username=mySecret;mypassword=mySecret;mypass=shouldkeep1;test_token=mySecret;password=123%@%_@!#^#@;use_password=mySecret;get_token=shouldkeep2;usetoken=shouldkeep3;passwrd=mySecret;")]

        // Announce URLs (passkeys) Magnet & Tracker
        [TestCase(@"magnet_uri"":""magnet:?xt=urn:btih:9pr04sgkillroyimaveql2tyu8xyui&dn=&tr=https%3a%2f%2fxxx.yyy%2f9pr04sg601233210IMAveQL2tyu8xyui%2fannounce""}")]
        [TestCase(@"magnet_uri"":""magnet:?xt=urn:btih:9pr04sgkillroyimaveql2tyu8xyui&dn=&tr=https%3a%2f%2fxxx.yyy%2ftracker.php%2f9pr04sg601233210IMAveQL2tyu8xyui%2fannounce""}")]
        [TestCase(@"magnet_uri"":""magnet:?xt=urn:btih:9pr04sgkillroyimaveql2tyu8xyui&dn=&tr=https%3a%2f%2fxxx.yyy%2fannounce%2f9pr04sg601233210IMAveQL2tyu8xyui""}")]
        [TestCase(@"magnet_uri"":""magnet:?xt=urn:btih:9pr04sgkillroyimaveql2tyu8xyui&dn=&tr=https%3a%2f%2fxxx.yyy%2fannounce.php%3fpasskey%3d9pr04sg601233210IMAveQL2tyu8xyui""}")]
        [TestCase(@"tracker"":""https://xxx.yyy/9pr04sg601233210IMAveQL2tyu8xyui/announce""}")]
        [TestCase(@"tracker"":""https://xxx.yyy/tracker.php/9pr04sg601233210IMAveQL2tyu8xyui/announce""}")]
        [TestCase(@"tracker"":""https://xxx.yyy/announce/9pr04sg601233210IMAveQL2tyu8xyui""}")]
        [TestCase(@"tracker"":""https://xxx.yyy/announce.php?passkey=9pr04sg601233210IMAveQL2tyu8xyui""}")]
        [TestCase(@"tracker"":""http://xxx.yyy/announce.php?passkey=9pr04sg601233210IMAveQL2tyu8xyui"",""info"":""http://xxx.yyy/info?a=b""")]

        // Notifiarr
        [TestCase(@"https://xxx.yyy/api/v1/notification/readarr/9pr04sg6-0123-3210-imav-eql2tyu8xyui")]

        // Discord
        [TestCase(@"https://discord.com/api/webhooks/mySecret")]
        [TestCase(@"https://discord.com/api/webhooks/mySecret/01233210")]

        // Telegram
        [TestCase(@"https://api.telegram.org/bot1234567890:mySecret/sendmessage: chat_id=123456&parse_mode=HTML&text=<text>")]
        [TestCase(@"https://api.telegram.org/bot1234567890:mySecret/")]

        public void should_clean_message(string message)
        {
            var cleansedMessage = CleanseLogMessage.Cleanse(message);

            cleansedMessage.Should().NotContain("mySecret");
            cleansedMessage.Should().NotContain("123%@%_@!#^#@");
            cleansedMessage.Should().NotContain("01233210");
        }

        [TestCase(@"[Info] Provider: token=mySecret&username=mySecret&mypassword=mySecret&mypass=shouldkeep1&test_token=mySecret&password=123%@%_@!#^#@&use_password=mySecret&get_token=shouldkeep2&usetoken=shouldkeep3&passwrd=mySecret;")]
        public void should_keep_message(string message)
        {
            var cleansedMessage = CleanseLogMessage.Cleanse(message);

            cleansedMessage.Should().NotContain("mySecret");
            cleansedMessage.Should().NotContain("123%@%_@!#^#@");
            cleansedMessage.Should().NotContain("01233210");

            cleansedMessage.Should().Contain("shouldkeep1");
            cleansedMessage.Should().Contain("shouldkeep2");
            cleansedMessage.Should().Contain("shouldkeep3");
        }

        //GoodReads
        [TestCase(@"{""signatureMethod"": ""hmacSha1"",""signatureTreatment"": ""escaped"",""type"": ""protectedResource"",""method"": ""GET"",""token"": ""mytoken"",""tokenSecret"": ""mytokensecret"",""requestUrl"": ""https://www.goodreads.com/review/list.xml"",""parameters"": {  ""_nc"": ""1"",  ""v"": ""2"",  ""id"": ""999999999"",  ""shelf"": ""currently-reading"",  ""per_page"": ""200"",  ""page"": ""1""}")]
        [TestCase(@"https://www.goodreads.com/series/311911?key=1234530f422f4aacb6b301233210aaaa&_nc=1&format=xml")]
        public void should_cleanGoodRead_message(string message)
        {
            var cleansedMessage = CleanseLogMessage.Cleanse(message);

            cleansedMessage.Should().NotContain("mytokensecret");
            cleansedMessage.Should().NotContain("mytoken");
        }

        [TestCase(@"Some message (from 32.2.3.5 user agent)")]
        [TestCase(@"Auth-Invalidated ip 32.2.3.5")]
        [TestCase(@"Auth-Success ip 32.2.3.5")]
        [TestCase(@"Auth-Logout ip 32.2.3.5")]
        public void should_clean_ipaddress(string message)
        {
            var cleansedMessage = CleanseLogMessage.Cleanse(message);

            cleansedMessage.Should().NotContain(".2.3.");
        }

        [TestCase(@"Some message (from 10.2.3.2 user agent)")]
        [TestCase(@"Auth-Unauthorized ip 32.2.3.5")]
        [TestCase(@"Auth-Failure ip 32.2.3.5")]
        public void should_not_clean_ipaddress(string message)
        {
            var cleansedMessage = CleanseLogMessage.Cleanse(message);

            cleansedMessage.Should().Be(message);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t\r\n ")]
        [TestCase("Imported 7 audiobook tracks in 3 seconds; no missing files.")]
        [TestCase("get_token=keep-token;usetoken=keep-flag;mypass=keep-password")]
        [TestCase("https://catalog.example/books?title=An%20Audiobook&offset=3&limit=10")]
        public void should_preserve_ordinary_messages_exactly(string message)
        {
            CleanseLogMessage.Cleanse(message).Should().Be(message);
        }

        [TestCase("GET https://catalog.example/books?key=first-secret&offset=3", "GET https://catalog.example/books?key=(removed)&offset=3")]
        [TestCase("https://catalog.example/books?authkey=first-secret&format=json", "https://catalog.example/books?authkey=(removed)&format=json")]
        [TestCase("https://catalog.example/books?passkey=first-secret&format=json", "https://catalog.example/books?passkey=(removed)&format=json")]
        [TestCase("https://catalog.example/books?access-token=first-secret&offset=3", "https://catalog.example/books?access-token=(removed)&offset=3")]
        [TestCase("/readarr/signalr/messages?access_token=first-secret&negotiateVersion=1", "/readarr/signalr/messages?access_token=(removed)&negotiateVersion=1")]
        [TestCase("https://catalog.example/books?APIKEY=first-secret&TOKEN=second-secret&limit=2", "https://catalog.example/books?APIKEY=(removed)&TOKEN=(removed)&limit=2")]
        [TestCase("{\"token\": \"first-secret\",\"tokenSecret\": \"second-secret\",\"method\": \"GET\"}", "{\"token\": \"(removed)\",\"tokenSecret\": \"(removed)\",\"method\": \"GET\"}")]
        [TestCase("https://notify.example/api/v1/notification/readarr/first-secret", "https://notify.example/api/v1/notification/readarr/(removed)")]
        [TestCase("https://discord.com/api/webhooks/first-secret/second-longer-secret", "https://discord.com/api/webhooks/(removed)/(removed)")]
        [TestCase("https://iptorrents.com/torrents/rss?u=first-secret;tp=second-longer-secret;l5;download", "https://iptorrents.com/torrents/rss?u=(removed);tp=(removed);l5;download")]
        [TestCase("https://tracker.example/0123456789abcdef0123456789abcdef/announce?left=3", "https://tracker.example/(removed)/announce?left=3")]
        [TestCase("magnet:?xt=urn:btih:keep-this-infohash&tr=https://tracker.example/announce/0123456789abcdef0123456789abcdef", "magnet:?xt=urn:btih:keep-this-infohash&tr=https://tracker.example/announce/(removed)")]
        [TestCase("Auth-Logout ip 198.51.100.42", "Auth-Logout ip 198.*.*.42")]
        public void should_replace_only_secret_captures_and_preserve_surrounding_context(string message, string expected)
        {
            CleanseLogMessage.Cleanse(message).Should().Be(expected);
        }

        [TestCase("User 'reader@example.test' is not authorized", "User '(removed)@example.test' is not authorized")]
        [TestCase("Sending mail to First.Last+tag_2@sub.example.test failed", "Sending mail to (removed)@sub.example.test failed")]
        [TestCase("Contact first@example.test and second@other.example.test", "Contact (removed)@example.test and (removed)@other.example.test")]
        [TestCase("Auth-Logout ip 198.51.100.42 username 'reader@example.test'", "Auth-Logout ip 198.*.*.42 username '(removed)@example.test'")]
        public void should_redact_email_local_parts_without_changing_domains_or_other_context(string message, string expected)
        {
            CleanseLogMessage.Cleanse(message).Should().Be(expected);
        }
    }
}
