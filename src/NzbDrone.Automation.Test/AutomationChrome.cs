using System;
using NUnit.Framework;
using OpenQA.Selenium.Chrome;

namespace NzbDrone.Automation.Test
{
    public static class AutomationChrome
    {
        public static ChromeDriver Create(bool isolatedSyntheticPage = false)
        {
            var options = new ChromeOptions();
            options.AddArguments("--headless", "--disable-background-networking", "--disable-component-update", "--no-first-run");
            var binary = Environment.GetEnvironmentVariable("BOOKSHELF_TEST_CHROME_BINARY");
            if (!string.IsNullOrWhiteSpace(binary))
            {
                options.BinaryLocation = binary;
            }

            // Some locked-down Linux test hosts disallow unprivileged user namespaces.
            // This opt-in is only available to the fixture whose entire page and
            // listener are owned synthetic data; never to the application suite.
            if (isolatedSyntheticPage && Environment.GetEnvironmentVariable("BOOKSHELF_TEST_CHROME_NO_SANDBOX") == "1")
            {
                options.AddArgument("--no-sandbox");
            }

            // Always use the restored ChromeDriver; do not silently download a
            // different driver or disable browser-version compatibility checks.
            var service = ChromeDriverService.CreateDefaultService(TestContext.CurrentContext.TestDirectory);
            return new ChromeDriver(service, options, TimeSpan.FromMinutes(3));
        }
    }
}
