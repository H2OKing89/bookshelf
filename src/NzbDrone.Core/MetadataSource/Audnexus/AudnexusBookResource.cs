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
        public string Language { get; set; }
        public string PublisherName { get; set; }
        public string Image { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public int? RuntimeLengthMin { get; set; }
        public List<AudnexusContributorResource> Authors { get; set; }
        public List<AudnexusContributorResource> Narrators { get; set; }
        public List<AudnexusGenreResource> Genres { get; set; }
        public AudnexusSeriesResource SeriesPrimary { get; set; }
        public AudnexusSeriesResource SeriesSecondary { get; set; }
        public string Rating { get; set; }
        public string Isbn { get; set; }
        public string FormatType { get; set; }
    }

    public class AudnexusContributorResource
    {
        public string Asin { get; set; }
        public string Name { get; set; }
    }

    public class AudnexusGenreResource
    {
        public string Name { get; set; }
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
    }
}
