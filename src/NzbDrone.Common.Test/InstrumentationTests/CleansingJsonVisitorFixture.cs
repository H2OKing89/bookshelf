using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Test.InstrumentationTests
{
    [TestFixture]
    public class CleansingJsonVisitorFixture
    {
        private const string Secret = "json-leaf-secret-12345";

        [Test]
        public void should_cleanse_nested_array_and_property_strings_without_changing_other_values()
        {
            var json = new JObject
            {
                ["message"] = "?apikey=" + Secret,
                ["items"] = new JArray(new JObject { ["message"] = "?access_token=" + Secret },
                    new JArray("password=" + Secret, 42, true, JValue.CreateNull()))
            };

            new CleansingJsonVisitor().Visit(json);

            json.ToString().Should().NotContain(Secret);
            json["items"][1][1].Value<int>().Should().Be(42);
            json["items"][1][2].Value<bool>().Should().BeTrue();
            json["items"][1][3].Type.Should().Be(JTokenType.Null);
        }

        [Test]
        public void malformed_json_text_in_a_string_should_still_be_cleansed()
        {
            var json = new JObject { ["message"] = "{malformed JSON ?apikey=" + Secret + "&page=2" };

            new CleansingJsonVisitor().Visit(json);

            json["message"].Value<string>().Should().Be("{malformed JSON ?apikey=(removed)&page=2");
        }

        [Test]
        public void oversized_string_leaves_should_not_fall_back_to_raw_json_text()
        {
            var text = new string('x', 70000) + "{malformed JSON ?apikey=" + Secret;
            var json = new JObject { ["items"] = new JArray(text, new JObject { ["message"] = text }) };

            new CleansingJsonVisitor().Visit(json);

            json.ToString().Should().NotContain(Secret).And.NotContain(new string('x', 100));
            json["items"][0].Value<string>().Should().Be("(removed, log message could not be safely cleansed)");
            json["items"][1]["message"].Value<string>().Should().Be("(removed, log message could not be safely cleansed)");
        }
    }
}
