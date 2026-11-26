using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Test.Common;
using OpenQA.Selenium;

namespace NzbDrone.Automation.Test
{
    [TestFixture]
    [AutomationTest]
    public abstract class AutomationTest
    {
        private NzbDroneRunner _runner;
        protected WebDriver driver;

        public AutomationTest()
        {
            new StartupContext();

            LogManager.Configuration = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget { Layout = "${level}: ${message} ${exception}" };
            LogManager.Configuration.AddTarget(consoleTarget.GetType().Name, consoleTarget);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Trace, consoleTarget));
        }

        [OneTimeSetUp]
        public void SmokeTestSetup()
        {
            driver = AutomationChrome.Create();

            driver.Manage().Window.Size = new System.Drawing.Size(1920, 1080);
            driver.Manage().Window.FullScreen();

            _runner = new NzbDroneRunner(LogManager.GetCurrentClassLogger(), null);
            _runner.Start(true);

            driver.Navigate().GoToUrl($"http://127.0.0.1:{_runner.Port}");

            var page = new PageBase(driver);
            page.WaitForNoSpinner();

            GetPageErrors().Should().BeEmpty();
        }

        protected IEnumerable<string> GetPageErrors()
        {
            return driver.FindElements(By.CssSelector("#errors div"))
                .Select(e => e.Text);
        }

        protected void TakeScreenshot(string name)
        {
            try
            {
                var image = (driver as ITakesScreenshot).GetScreenshot();
                image.SaveAsFile($"./{name}_test_screenshot.png");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save screenshot {name}, {ex.Message}");
            }
        }

        [OneTimeTearDown]
        public void SmokeTestTearDown()
        {
            try
            {
                driver?.Dispose();
            }
            finally
            {
                _runner?.Kill();
            }
        }

        [TearDown]
        public void AutomationTearDown()
        {
            GetPageErrors().Should().BeEmpty();
        }
    }
}
