using GourmetClientApp.Model;
using GourmetClientApp.Utils;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GourmetClientApp.Network;

public partial class VentopayWebClient : WebClientBase
{
    private const string WebUrl = "https://my.ventopay.com/mocca.website/";
    private const string PageNameLogin = "Login.aspx";
    private const string PageNameLogout = "Ausloggen.aspx";
    private const string PageNameTransactions = "Transaktionen.aspx";
    private const string PageNameTransactionDetails = "Rechnung.aspx";
    private const string CompanyIdTrumpf = "0da8d3ec-0178-47d5-9ccd-a996f04acb61";

    [GeneratedRegex(@"<a\s+href=""Ausloggen.aspx"">")]
    private static partial Regex LoginSuccessfulRegex();

    [GeneratedRegex(@"(\d+)\.\s+([a-zA-z]+)\s+(\d+)\s+-\s+(\d+):(\d+)")]
    private static partial Regex TransactionDateTimeRegex();

    protected override async Task<bool> LoginImpl(string userName, string password)
    {
        string requestUrl = GetWebUrlForPage(PageNameLogin);
        using HttpResponseMessage loginPageResponse = await ExecuteGetRequest(requestUrl);
        string loginPageContent = await ReadResponseContent(loginPageResponse);

        Dictionary<string, string> parameters;
        try
        {
            parameters = ParseAspxParameters(loginPageContent);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException(
                "Error parsing the login HTML", GetRequestUriString(loginPageResponse), loginPageContent, exception);
        }

        parameters.Add("DropDownList1", CompanyIdTrumpf);
        parameters.Add("TxtUsername", userName);
        parameters.Add("TxtPassword", password);
        parameters.Add("BtnLogin", "Login");
        parameters.Add("languageRadio", "DE");

        using HttpResponseMessage loginResponse = await ExecuteFormPostRequest(requestUrl, parameters);
        string loginContent = await ReadResponseContent(loginResponse);

        return LoginSuccessfulRegex().IsMatch(loginContent);
    }

    protected override async Task LogoutImpl()
    {
        string requestUrl = GetWebUrlForPage(PageNameLogout);

        try
        {
            using HttpResponseMessage response = await ExecuteGetRequest(requestUrl);
        }
        catch (GourmetRequestException)
        {
            // Ignore this exception during logout. The session on the server side may be not cleared, but it will be
            // removed from the server eventually.
        }
    }

    public async Task<IReadOnlyList<BillingPosition>> GetBillingPositions(DateTime fromDate, DateTime toDate, IProgress<int> progress)
    {
        var parameters = new Dictionary<string, string>
        {
            { "fromDate", fromDate.ToString("dd.MM.yyyy") },
            { "untilDate", toDate.ToString("dd.MM.yyyy") }
        };

        string requestUrl = GetWebUrlForPage(PageNameTransactions);
        using HttpResponseMessage response = await ExecuteGetRequest(requestUrl, parameters);
        string htmlContent = await ReadResponseContent(response);

        try
        {
            return await GetBillingPositionsFromHtml(htmlContent, progress);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException("Error parsing the billing HTML", GetRequestUriString(response), htmlContent, exception);
        }
    }

    private static string GetWebUrlForPage(string pageName)
    {
        return $"{WebUrl}{pageName}";
    }

    private static Dictionary<string, string> ParseAspxParameters(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        HtmlNode viewStateNode = document.DocumentNode.GetSingleNode("//input[@id='__VIEWSTATE']");
        HtmlNode viewStateGeneratorNode = document.DocumentNode.GetSingleNode("//input[@id='__VIEWSTATEGENERATOR']");
        HtmlNode eventValidationNode = document.DocumentNode.GetSingleNode("//input[@id='__EVENTVALIDATION']");

        var parameters = new Dictionary<string, string>
        {
            { viewStateNode.GetAttributeValue("id"), viewStateNode.GetAttributeValue("value") },
            { viewStateGeneratorNode.GetAttributeValue("id"), viewStateGeneratorNode.GetAttributeValue("value") },
            { eventValidationNode.GetAttributeValue("id"), eventValidationNode.GetAttributeValue("value") }
        };

        return parameters;
    }

    private async Task<IReadOnlyList<BillingPosition>> GetBillingPositionsFromHtml(string html, IProgress<int> progress)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var billingPositions = new List<BillingPosition>();
        HtmlNode contentNode = document.DocumentNode.GetSingleNode("//div[@class='content']");
        HtmlNode[] rowNodes = contentNode.GetNodes(".//div[@class='transact']").ToArray();
        double rowCounter = 0;

        foreach (var rowNode in rowNodes)
        {
            if (!rowNode.TryGetAttributeValue("id", out string? transactionId) || string.IsNullOrEmpty(transactionId))
            {
                continue;
            }

            IReadOnlyList<BillingPosition> entries = await GetBillingPositionsFromTransaction(transactionId);
            billingPositions.AddRange(entries);

            rowCounter++;
            progress.Report((int)((rowCounter / rowNodes.Length) * 100));
        }

        return billingPositions;
    }

    private async Task<IReadOnlyList<BillingPosition>> GetBillingPositionsFromTransaction(string transactionId)
    {
        var parameters = new Dictionary<string, string>
        {
            { "id", transactionId }
        };

        string requestUrl = GetWebUrlForPage(PageNameTransactionDetails);
        using HttpResponseMessage response = await ExecuteGetRequest(requestUrl, parameters);
        string htmlContent = await ReadResponseContent(response);

        var document = new HtmlDocument();
        document.LoadHtml(htmlContent);

        HtmlNode contentNode = document.DocumentNode.GetSingleNode("//div[@id='rechnung']");
        HtmlNode restaurantInfoNode = contentNode.GetSingleNode(".//span[@id='ContentPlaceHolder1_LblRestaurantInfo']");
        RestaurantInfo restaurantInfo = GetRestaurantInfo(restaurantInfoNode.InnerHtml);

        if (restaurantInfo.Name.Contains("Gourmet") && !restaurantInfo.Location.Contains("Kaffeeautomat"))
        {
            // Gourmet transactions are received from the Gourmet website (except for the coffee machine)
            return [];
        }

        HtmlNode dateNode = contentNode.GetSingleNode(".//span[@id='ContentPlaceHolder1_LblTimestamp']");
        HtmlNode tableBodyNode = contentNode.GetSingleNode(".//div[@class='rechnungpart']//table//tbody");

        DateTime dateTime = GetDateTimeFromTransactionDateString(dateNode.GetInnerText());
        var billingPositions = new List<BillingPosition>();

        foreach (HtmlNode rowNode in tableBodyNode.GetNodes(".//tr[not(contains(@class, 'rechnungsdetail'))]"))
        {
            HtmlNode[] columnNodes = rowNode.GetNodes(".//td").ToArray();

            HtmlNode countNode = columnNodes[0];
            HtmlNode positionNameNode = columnNodes[1];
            HtmlNode costNode = columnNodes[4];

            string positionName = positionNameNode.GetInnerText();
            string countString = countNode.GetInnerText().Replace("x", string.Empty).Trim();
            string costString = costNode.GetInnerText();

            if (!int.TryParse(countString, out int count))
            {
                throw new FormatException($"Count '{countString}' has an invalid format");
            }

            if (!double.TryParse(costString, new CultureInfo("de-DE"), out double cost))
            {
                throw new FormatException($"Cost '{costString}' has an invalid format");
            }

            var positionType = positionName switch
            {
                "Snackware" => BillingPositionType.Menu,
                _ => BillingPositionType.Drink
            };

            billingPositions.Add(new BillingPosition(dateTime, positionType, positionName, count, cost));
        }

        return billingPositions;
    }

    private static DateTime GetDateTimeFromTransactionDateString(string dateString)
    {
        Match match = TransactionDateTimeRegex().Match(dateString);
        if (!match.Success)
        {
            throw new FormatException($"Date string '{dateString}' has an invalid format");
        }

        string dayString = match.Groups[1].Value;
        string monthString = match.Groups[2].Value;
        string yearString = match.Groups[3].Value;
        string hourString = match.Groups[4].Value;
        string minuteString = match.Groups[5].Value;

        if (!int.TryParse(dayString, out int day))
        {
            throw new FormatException($"Could not parse value '{dayString}' for day as integer");
        }

        int month = monthString switch
        {
            "Jan" => 1,
            "Feb" => 2,
            "Mrz" => 3,
            "Apr" => 4,
            "Mai" => 5,
            "Jun" => 6,
            "Jul" => 7,
            "Aug" => 8,
            "Sep" => 9,
            "Okt" => 10,
            "Nov" => 11,
            "Dez" => 12,
            _ => throw new FormatException($"Invalid month value: '{monthString}'")
        };

        if (!int.TryParse(yearString, out int year))
        {
            throw new FormatException($"Could not parse value '{yearString}' for year as integer");
        }

        if (!int.TryParse(hourString, out int hour))
        {
            throw new FormatException($"Could not parse value '{hourString}' for hour as integer");
        }

        if (!int.TryParse(minuteString, out int minute))
        {
            throw new FormatException($"Could not parse value '{minuteString}' for minute as integer");
        }

        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();
    }

    private static RestaurantInfo GetRestaurantInfo(string infoString)
    {
        string[] parts = infoString.Split("<br>");
        return new RestaurantInfo(Name: parts[0], Location: parts[3]);
    }

    private static bool IsParseException(Exception exception)
    {
        return exception is GourmetHtmlNodeException || exception is FormatException;
    }

    private record RestaurantInfo(string Name, string Location);
}