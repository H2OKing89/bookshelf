using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using NzbDrone.Host;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class BootstrapLogStorageFixture
    {
        [TestCase(null, null, true)]
        [TestCase(null, true, true)]
        [TestCase(null, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, true)]
        [TestCase(false, null, false)]
        public void host_registration_should_use_the_same_option_precedence_as_the_config_provider(bool? option, bool? saved, bool expected)
        {
            var values = new Dictionary<string, string>();
            if (option.HasValue)
            {
                values["Readarr:Log:DbEnabled"] = option.ToString();
            }

            if (saved.HasValue)
            {
                values["LogDbEnabled"] = saved.ToString();
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var method = typeof(Bootstrap).GetMethod("IsLogDatabaseEnabled", BindingFlags.NonPublic | BindingFlags.Static);

            ((bool)method.Invoke(null, new object[] { configuration })).Should().Be(expected);
        }
    }
}
