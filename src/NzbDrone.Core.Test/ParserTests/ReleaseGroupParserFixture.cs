using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class ReleaseGroupParserFixture : CoreTest
    {
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-ENTiTLED", "ENTiTLED")]
        [TestCase("[ www.Torrenting.com ] - Olafur.Arnalds-Remember-WEB-2018-ENTiTLED", "ENTiTLED")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-ENTiTLED [eztv]-[rarbg.com]", "ENTiTLED")]
        [TestCase("7s-atlantis-128.mp3", null)]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-ENTiTLED-Pre", "ENTiTLED")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-ENTiTLED-postbot", "ENTiTLED")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-ENTiTLED-xpost", "ENTiTLED")]

        //[TestCase("", "")]
        public void should_parse_release_group(string title, string expected)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().Be(expected);
        }

        [Test]
        [Ignore("Track name parsing needs to be worked on")]
        public void should_not_include_extension_in_release_group()
        {
            const string path = @"C:\Test\Doctor.Who.2005.s01e01.internal.bdrip.x264-archivist.mkv";

            Parser.Parser.ParseMusicPath(path).ReleaseGroup.Should().Be("archivist");
        }

        [TestCase("Olafur.Arnalds-Remember-WEB-2018-SKGTV English", "SKGTV")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-SKGTV_English", "SKGTV")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-SKGTV.English", "SKGTV")]

        //[TestCase("", "")]
        public void should_not_include_language_in_release_group(string title, string expected)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().Be(expected);
        }

        [TestCase("Olafur.Arnalds-Remember-WEB-2018-EVL-RP", "EVL")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-EVL-RP-RP", "EVL")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-EVL-Obfuscated", "EVL")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-xHD-NZBgeek", "xHD")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-DIMENSION-NZBgeek", "DIMENSION")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-xHD-1", "xHD")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-DIMENSION-1", "DIMENSION")]
        [TestCase("Olafur.Arnalds-Remember-WEB-2018-EVL-Scrambled", "EVL")]
        public void should_not_include_repost_in_release_group(string title, string expected)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().Be(expected);
        }

        [TestCase("[FFF] Invaders of the Rokujouma!! - S01E11 - Someday, With Them", "FFF")]
        [TestCase("[HorribleSubs] Invaders of the Rokujouma!! - S01E12 - Invasion Going Well!!", "HorribleSubs")]
        [TestCase("[Anime-Koi] Barakamon - S01E06 - Guys From Tokyo", "Anime-Koi")]
        [TestCase("[Anime-Koi] Barakamon - S01E07 - A High-Grade Fish", "Anime-Koi")]
        [TestCase("[Anime-Koi] Kami-sama Hajimemashita 2 - 01 [h264-720p][28D54E2C]", "Anime-Koi")]

        //[TestCase("Tokyo.Ghoul.02x01.013.HDTV-720p-Anime-Koi", "Anime-Koi")]
        //[TestCase("", "")]
        public void should_parse_anime_release_groups(string title, string expected)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().Be(expected);
        }

        [TestCase("Author.Book.M4B-Team-One", "Team-One")]
        [TestCase("Author.Book.M4B-Team-One.m4b", "Team-One")]
        [TestCase("Author.Book.MP3-Team-One.mp3", "Team-One")]
        [TestCase("Author.Book.FLAC-Team-One.flac", "Team-One")]
        [TestCase("Author.Book.OPUS-Team-One.opus", "Team-One")]
        [TestCase("Author.Book.M4B-Báleygr", "Báleygr")]
        [TestCase("Author.Book.M4B-Hveðrungr", "Hveðrungr")]
        [TestCase("Author.Book.M4B [AudioCrew]", "AudioCrew")]
        [TestCase("Author.Book.M4B [Audio-Crew].m4b", "Audio-Crew")]
        [TestCase("Author.Book.M4B-Crew-RePACKPOST", "Crew")]
        [TestCase("Author.Book.M4B-Crew-AlteZachen", "Crew")]
        [TestCase("Author.Book.M4B-Crew-[N-Z-B]-xpost", "Crew")]
        [TestCase("Author.Book.M4B-Crew [rarbg]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [publichd]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [eztv]", "Crew")]
        [TestCase("Author.Book.M4B-2026-Crew", "Crew")]
        [TestCase("Author.Book.M4B-MP3-Crew", "Crew")]
        [TestCase("Author-Book-MP3-Team-One", "Team-One")]
        [TestCase("Author-Book-WEB-2026-Team-One", "Team-One")]
        [TestCase("Author.Book.M4B-Crew [FLAC]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [deadbeef]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [Part01]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [HE-AAC]", "Crew")]
        [TestCase("Author.Book.M4B-Crew-FLAC", "Crew")]
        [TestCase("Author.Book.M4B-Crew [English]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [ENG]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [EN]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [en-US]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [French]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [Unabridged]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [Multi]", "Crew")]
        [TestCase("Author.Book.M4B-Crew [HE-AAC] [FLAC] [English]", "Crew")]
        [TestCase("Author.Book.M4B [AudioCrew] [English]", "AudioCrew")]
        [TestCase("Author.Book.M4B [FRA-Crew]", "FRA-Crew")]
        public void should_parse_audiobook_release_groups(string title, string expected)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().Be(expected);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Author.Book.HE-AAC")]
        [TestCase("Author.Book.AAC-LC")]
        [TestCase("Author.Book.M4B-HE-AACv2")]
        [TestCase("Author.Book.M4B-xHE-AAC")]
        [TestCase("Author.Book.M4B-EAC3")]
        [TestCase("Author.Book.M4B-OPUS")]
        [TestCase("Author.Book.M4B-FLAC")]
        [TestCase("Author.Book.M4B-256kbps")]
        [TestCase("Author.Book.M4B-44kHz")]
        [TestCase("Author.Book.M4B-24-bit")]
        [TestCase("Author.Book.M4B-2026-11-15")]
        [TestCase("Author.Book.M4B-Part01")]
        [TestCase("Author.Book.M4B-Part-01")]
        [TestCase("Author.Book.M4B-CD1")]
        [TestCase("Author.Book.M4B-deadbeef")]
        [TestCase("Author.Book.M4B [deadbeef]")]
        [TestCase("Author.Book.M4B [FLAC]")]
        [TestCase("Author.Book.M4B [English]")]
        [TestCase("Author.Book.M4B [ENG]")]
        [TestCase("Author.Book.M4B [Unabridged]")]
        public void should_not_parse_audio_metadata_as_release_group(string title)
        {
            Parser.Parser.ParseReleaseGroup(title).Should().BeNull();
        }
    }
}
