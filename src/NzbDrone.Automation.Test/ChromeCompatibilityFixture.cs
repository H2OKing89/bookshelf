using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;
using OpenQA.Selenium;

namespace NzbDrone.Automation.Test
{
    // Explicitly selected by the owned browser validation run; this fixture does
    // not start/stop a Bookshelf instance or use a configured service.
    [AutomationTest]
    public class ChromeCompatibilityFixture
    {
        [Test]
        public async Task bundled_chromedriver_navigates_waits_and_captures_png()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            await using var app = builder.Build();
            app.Run(async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("<html><head><title>Owned automation</title></head><body>" +
                    "<a href='/library'>Library</a><div class='followingBalls'>Loading</div>" +
                    "<h1 id='page'>" + (context.Request.Path == "/library" ? "Library" : "Home") + "</h1>" +
                    "<script>setTimeout(()=>document.querySelector('.followingBalls').remove(), 500)</script></body></html>");
            });
            await app.StartAsync();
            var output = Path.Combine(Path.GetTempPath(), $"bookshelf-selenium-{Guid.NewGuid():N}.png");
            try
            {
                using var driver = AutomationChrome.Create(isolatedSyntheticPage: true);
                driver.Manage().Window.Size = new System.Drawing.Size(1200, 900);
                driver.Navigate().GoToUrl(app.Urls.Single());
                var page = new PageBase(driver);
                page.WaitForNoSpinner();
                page.Find(By.Id("page")).Text.Should().Be("Home");

                page.LibraryNavIcon.Click();
                page.WaitForNoSpinner();
                page.Find(By.Id("page")).Text.Should().Be("Library");
                new Uri(driver.Url).AbsolutePath.Should().Be("/library");
                driver.GetScreenshot().SaveAsFile(output);
                File.ReadAllBytes(output).Take(8).Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                TestContext.Progress.WriteLine($"Owned browser version: {driver.Capabilities.GetCapability("browserVersion")}");
            }
            finally
            {
                File.Delete(output);
                await app.StopAsync();
            }
        }
    }
}
