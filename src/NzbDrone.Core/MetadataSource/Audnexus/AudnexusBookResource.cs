using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Audnexus
{
    public class AudnexusBookResource
    {
        public string Asin { get; set; }
        public string Region { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Description { get; set; }
        public string Summary { get; set; }
        public bool DescriptionIsSummary { get; set; }
        public string Language { get; set; }
        public string PublisherName { get; set; }
        public string Image { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public int? RuntimeLengthMin { get; set; }
        public int? Copyright { get; set; }
        public bool? IsAdult { get; set; }
        public string LiteratureType { get; set; }
        public List<AudnexusContributorResource> Authors { get; set; }
        public List<AudnexusContributorResource> Narrators { get; set; }
        public List<AudnexusGenreResource> Genres { get; set; }
        public AudnexusSeriesResource SeriesPrimary { get; set; }
        public AudnexusSeriesResource SeriesSecondary { get; set; }
        public string Rating { get; set; }
        public string Isbn { get; set; }
        public string FormatType { get; set; }
        public string ContentType { get; set; }
        public string ContentDeliveryType { get; set; }
        public string PublicationType { get; set; }
    }

    public class AudnexusContributorResource
    {
        public string Asin { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }

    public class AudnexusGenreResource
    {
        public string Asin { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class AudnexusSeriesResource : AudnexusContributorResource
    {
        public string Position { get; set; }
    }

    public class AudnexusAuthorResource : AudnexusContributorResource
    {
        public string Region { get; set; }
        public string Description { get; set; }
        public string Image { get; set; }
        public List<AudnexusGenreResource> Genres { get; set; }
        public List<AudnexusContributorResource> Similar { get; set; }
    }

    public class AudnexusChapterResource
    {
        public string Asin { get; set; }
        public string Region { get; set; }
        public long? RuntimeLengthMs { get; set; }
        public long? BrandIntroDurationMs { get; set; }
        public long? BrandOutroDurationMs { get; set; }
        public bool? IsAccurate { get; set; }
        public List<AudnexusChapter> Chapters { get; set; }
    }

    public class AudnexusChapter
    {
        public string Title { get; set; }
        public int? Ordinal { get; set; }
        public long StartOffsetMs { get; set; }
        public long LengthMs { get; set; }
    }
}
