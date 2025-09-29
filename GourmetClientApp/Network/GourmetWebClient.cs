﻿using GourmetClientApp.Model;
using GourmetClientApp.Network.GourmetApi;
using GourmetClientApp.Utils;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GourmetClientApp.Network;

public partial class GourmetWebClient : WebClientBase
{
    private const string WebUrl = "https://alaclickneu.gourmet.at/";
    private const string PageNameStart = "start";
    private const string PageNameMenu = "menus";
    private const string PageNameOrderedMenu = "bestellungen";

    [GeneratedRegex(@"<a href=""https://alaclickneu.gourmet.at/einstellungen/"" class=""navbar-link"">")]
    private static partial Regex LoginSuccessfulRegex();

    [GeneratedRegex(@"MENÜ\s+([I]{1,3})")]
    private static partial Regex MenuNumberRegex();

    protected override async Task<bool> LoginImpl(string userName, string password)
    {
        string ufprtValue = await GetUfprtValueFromPage(PageNameStart, "//div[@class='login']//form");

        var parameters = new Dictionary<string, string>
        {
            {"Username", userName},
            {"Password", password},
            {"RememberMe", "false"},
            {"ufprt", ufprtValue}
        };

        using HttpResponseMessage response = await ExecuteFormPostRequest(WebUrl, parameters);
        string httpContent = await ReadResponseContent(response);

        // Login is successful if link to user settings is received
        return LoginSuccessfulRegex().IsMatch(httpContent);
    }

    protected override async Task LogoutImpl()
    {
        try
        {
            string ufprtValue = await GetUfprtValueFromPage(PageNameStart, "//form[.//button[@id='btnHeaderLogout']]");

            var parameters = new Dictionary<string, string>
            {
                { "ufprt", ufprtValue }
            };

            using HttpResponseMessage response = await ExecutePostRequestForPage(PageNameStart, parameters);
        }
        catch (Exception exception) when (exception is GourmetRequestException || exception is GourmetParseException)
        {
            // Ignore these exceptions during logout. The session on the server side may be not cleared, but it will
            // be removed from the server eventually.
        }
    }

    public async Task<GourmetMenuResult> GetMenus()
    {
        // The page contains the menu elements twice (once for the desktop UI and once for the mobile UI).
        // By using a HashSet, it is ensured that the same menu is only added once.
        var parsedMenus = new HashSet<GourmetMenu>();
        GourmetUserInformation? userInformation = null;

        // Set 10 pages as upper limit
        var maxPages = 10;
        var currentPage = 0;

        do
        {
            var pageParameter = new Dictionary<string, string>
            {
                { "page", currentPage.ToString() }
            };

            using HttpResponseMessage response = await ExecuteGetRequestForPage(PageNameMenu, pageParameter);
            string httpContent = await ReadResponseContent(response);

            var document = new HtmlDocument();
            document.LoadHtml(httpContent);

            try
            {
                userInformation ??= ParseHtmlForUserInformation(document);

                foreach (GourmetMenu parsedMenu in ParseGourmetMenuHtml(document))
                {
                    parsedMenus.Add(parsedMenu);
                }

                if (!HasNextPageButton(document))
                {
                    break;
                }
            }
            catch (Exception exception) when (IsParseException(exception))
            {
                throw new GourmetParseException("Error parsing the menu HTML", GetRequestUriString(response), httpContent, exception);
            }

            currentPage++;
        } while (currentPage < maxPages);

        return new GourmetMenuResult(userInformation, parsedMenus);
    }

    public async Task<GourmetOrderedMenuResult> GetOrderedMenus()
    {
        using HttpResponseMessage response = await ExecuteGetRequestForPage(PageNameOrderedMenu);
        string httpContent = await ReadResponseContent(response);

        var document = new HtmlDocument();
        document.LoadHtml(httpContent);

        try
        {
            bool isOrderChangeForTodayPossible = !HasNoMoreOrdersForTodayErrorMessage(document);
            GourmetOrderedMenu[] orderedMenus = ParseOrderedGourmetMenuHtml(document, isOrderChangeForTodayPossible).ToArray();

            return new GourmetOrderedMenuResult(isOrderChangeForTodayPossible, orderedMenus);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException("Error parsing the ordered menu HTML", GetRequestUriString(response), httpContent, exception);
        }
    }

    public async Task<GourmetApiResult> AddMenuToOrderedMenu(GourmetUserInformation userInformation, GourmetMenu menu)
    {
        var requestDate = new AddToMenuesCartDate
        {
            DateString = menu.Day.ToString("MM-dd-yyyy"),
            MenuIds = [menu.MenuId]
        };

        var requestObject = new AddToMenuesCartRequest
        {
            EaterId = userInformation.EaterId,
            ShopModelId = userInformation.ShopModelId,
            StaffGroupId = userInformation.StaffGroupId,
            Dates = [requestDate]
        };

        using HttpResponseMessage response = await ExecuteJsonPostRequest($"{WebUrl}umbraco/api/AlaCartApi/AddToMenuesCart", requestObject);
        var responseObject = await ParseJsonResponseObject<AddToMenuesCartResponse>(response);

        return new GourmetApiResult(responseObject.Success, responseObject.Message);
    }

    public async Task CancelOrders(IReadOnlyList<GourmetOrderedMenu> orderedMenus)
    {
        if (orderedMenus.Count == 0)
        {
            return;
        }

        (HtmlDocument document, string resultUriInfo, string resultHttpContent) = await EnterOrderedMenuEditMode();

        foreach (GourmetOrderedMenu orderedMenu in orderedMenus)
        {
            Dictionary<string, string> cancelOrderParameters;
            try
            {
                cancelOrderParameters = GetCancelOrderParameters(document, orderedMenu.PositionId);
            }
            catch (Exception exception) when (IsParseException(exception))
            {
                throw new GourmetParseException("Error parsing the ordered menu HTML", resultUriInfo, resultHttpContent, exception);
            }

            using HttpResponseMessage cancelOrderResponse = await ExecutePostRequestForPage(PageNameOrderedMenu, cancelOrderParameters);

            // Reuse response HTML to cancel next order
            resultHttpContent = await ReadResponseContent(cancelOrderResponse);
            resultUriInfo = GetRequestUriString(cancelOrderResponse);
            document.LoadHtml(resultHttpContent);
        }
    }

    public async Task ConfirmOrder()
    {
        using HttpResponseMessage orderedMenuResponse = await ExecuteGetRequestForPage(PageNameOrderedMenu);
        string orderedMenuHttpContent = await ReadResponseContent(orderedMenuResponse);

        var document = new HtmlDocument();
        document.LoadHtml(orderedMenuHttpContent);

        Dictionary<string, string> confirmOrderParameters;
        try
        {
            if (!IsOrderedMenuPageEditModeActive(document))
            {
                // Only needs to be confirmed if edit mode is active
                return;
            }

            confirmOrderParameters = GetToggleOrderMenuEditModeParameters(document);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException(
                "Error parsing the ordered menu HTML", GetRequestUriString(orderedMenuResponse), orderedMenuHttpContent, exception);
        }

        using HttpResponseMessage confirmResponse = await ExecutePostRequestForPage(PageNameOrderedMenu, confirmOrderParameters);
    }

    public async Task<IReadOnlyList<BillingPosition>> GetBillingPositions(int month, int year, IProgress<int> progress)
    {
        using HttpResponseMessage billingResponse = await ExecuteGetRequestForPage(PageNameStart);
        string httpContent = await ReadResponseContent(billingResponse);

        var document = new HtmlDocument();
        document.LoadHtml(httpContent);

        GourmetUserInformation userInformation;
        try
        {
            userInformation = ParseHtmlForUserInformation(document);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException("Error parsing the start page HTML", GetRequestUriString(billingResponse), httpContent, exception);
        }

        // Treat reading the user information as 50%, since it is a separate request.
        progress.Report(50);

        var inputDate = new DateTime(year, month, 1);
        var currentDate = DateTime.Now;
        int monthsDifference = (currentDate.Year - inputDate.Year) * 12 + currentDate.Month - inputDate.Month;

        var billingRequest = new BillingRequest
        {
            EaterId = userInformation.EaterId,
            ShopModelId = userInformation.ShopModelId,
            CheckLastMonthNumber = monthsDifference.ToString()
        };

        using HttpResponseMessage apiResponse = await ExecuteJsonPostRequest($"{WebUrl}umbraco/api/AlaMyBillingApi/GetMyBillings", billingRequest);
        var bills = await ParseJsonResponseObject<Bill[]>(apiResponse);

        var result = new List<BillingPosition>();

        foreach (Bill bill in bills)
        {
            foreach (BillingItem billingItem in bill.BillingItems)
            {
                double cost = billingItem.TotalCost - billingItem.Subsidy;
                result.Add(new BillingPosition(bill.BillDate, BillingPositionType.Menu, billingItem.Description, billingItem.Count, cost));
            }
        }

        progress.Report(100);
        return result;
    }

    private async Task<(HtmlDocument Document, string ResultUriInfo, string ResultHttpContent)> EnterOrderedMenuEditMode()
    {
        using HttpResponseMessage orderedMenuResponse = await ExecuteGetRequestForPage(PageNameOrderedMenu);
        string orderedMenuHttpContent = await ReadResponseContent(orderedMenuResponse);

        var orderedMenuDocument = new HtmlDocument();
        orderedMenuDocument.LoadHtml(orderedMenuHttpContent);

        Dictionary<string, string> enterEditModeParameters;
        try
        {
            if (IsOrderedMenuPageEditModeActive(orderedMenuDocument))
            {
                // Already in edit mode
                return (orderedMenuDocument, GetRequestUriString(orderedMenuResponse), orderedMenuHttpContent);
            }

            enterEditModeParameters = GetToggleOrderMenuEditModeParameters(orderedMenuDocument);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException(
                "Error parsing the ordered menu HTML", GetRequestUriString(orderedMenuResponse), orderedMenuHttpContent, exception);
        }

        using HttpResponseMessage enterEditModeResponse = await ExecutePostRequestForPage(PageNameOrderedMenu, enterEditModeParameters);
        string enterEditModeHttpContent = await ReadResponseContent(enterEditModeResponse);

        var enterEditModeDocument = new HtmlDocument();
        enterEditModeDocument.LoadHtml(enterEditModeHttpContent);

        bool editModeActivated;
        try
        {
            editModeActivated = IsOrderedMenuPageEditModeActive(enterEditModeDocument);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException(
                "Error parsing the ordered menu HTML", GetRequestUriString(orderedMenuResponse), orderedMenuHttpContent, exception);
        }

        if (!editModeActivated)
        {
            throw new GourmetRequestException("Cannot enter edit mode of ordered menus", GetRequestUriString(enterEditModeResponse));
        }

        return (enterEditModeDocument, GetRequestUriString(enterEditModeResponse), enterEditModeHttpContent);
    }

    private async Task<string> GetUfprtValueFromPage(string pageName, string formXPath)
    {
        using HttpResponseMessage response = await ExecuteGetRequestForPage(pageName);
        string httpContent = await ReadResponseContent(response);

        var document = new HtmlDocument();
        document.LoadHtml(httpContent);

        try
        {
            HtmlNode formNode = document.DocumentNode.GetSingleNode(formXPath);
            return ParseUfprtValue(formNode);
        }
        catch (Exception exception) when (IsParseException(exception))
        {
            throw new GourmetParseException("Error parsing the 'ufprt' value", GetRequestUriString(response), httpContent, exception);
        }
    }

    private static string ParseUfprtValue(HtmlNode formNode)
    {
        HtmlNode ufprtNode = formNode.GetSingleNode(".//input[@name='ufprt']");
        return ufprtNode.GetAttributeValue("value");
    }

    private Task<HttpResponseMessage> ExecuteGetRequestForPage(string pageName, IReadOnlyDictionary<string, string>? urlParameters = null)
    {
        return ExecuteGetRequest($"{WebUrl}{pageName}/", urlParameters);
    }

    private Task<HttpResponseMessage> ExecutePostRequestForPage(string pageName, IReadOnlyDictionary<string, string> formParameters)
    {
        return ExecuteFormPostRequest($"{WebUrl}{pageName}/", formParameters);
    }

    private static GourmetUserInformation ParseHtmlForUserInformation(HtmlDocument document)
    {
        HtmlNode loginNameNode = document.DocumentNode.GetSingleNode("//div[@class='userfield']//span[@class='loginname']");
        HtmlNode shopModelNode = document.DocumentNode.GetSingleNode("//input[@id='shopModel']");
        HtmlNode eaterNode = document.DocumentNode.GetSingleNode("//input[@id='eater']");
        HtmlNode staffGroupNode = document.DocumentNode.GetSingleNode("//input[@id='staffGroup']");

        string nameOfUser = loginNameNode.GetInnerText();
        string shopModelId = shopModelNode.GetAttributeValue("value");
        string eaterId = eaterNode.GetAttributeValue("value");
        string staffGroupId = staffGroupNode.GetAttributeValue("value");

        return new GourmetUserInformation(nameOfUser, shopModelId, eaterId, staffGroupId);
    }

    private static IEnumerable<GourmetMenu> ParseGourmetMenuHtml(HtmlDocument document)
    {
        foreach (HtmlNode menuNode in document.DocumentNode.GetNodes("//div[@class='meal']"))
        {
            HtmlNode detailNode = menuNode.GetSingleNode(".//div[@class='open_info menu-article-detail']");
            string positionId = detailNode.GetAttributeValue("data-id");
            DateTime day = ParseMenuDateString(detailNode.GetAttributeValue("data-date"));
            string title = menuNode.GetSingleNode(".//div[@class='title']").GetChildNodeAtIndex(0).GetInnerText();
            string subTitle = menuNode.GetSingleNode(".//div[@class='subtitle']").GetInnerText();
            char[] allergens = ParseAllergens(menuNode.GetSingleNode(".//li[@class='allergen']").GetInnerText());
            bool isAvailable = menuNode.ContainsNode(".//input[@type='checkbox' and @class='menu-clicked']");
            var category = GourmetMenuCategory.Unknown;
            string upperTitle = title.ToUpperInvariant();

            Match menuNumberMatch = MenuNumberRegex().Match(upperTitle);
            if (menuNumberMatch.Success)
            {
                category = menuNumberMatch.Groups[1].Value switch
                {
                    "I" => GourmetMenuCategory.Menu1,
                    "II" => GourmetMenuCategory.Menu2,
                    "III" => GourmetMenuCategory.Menu3,
                    _ => GourmetMenuCategory.Unknown
                };
            }
            else if (upperTitle == "SUPPE & SALAT")
            {
                category = GourmetMenuCategory.SoupAndSalad;
            }

            yield return new GourmetMenu(day, category, positionId, title, subTitle, allergens, isAvailable);
        }
    }

    private static DateTime ParseMenuDateString(string dateString)
    {
        // Sample value: "06-30-2025"
        string[] splitValue = dateString.Split('-');
        if (splitValue.Length != 3)
        {
            throw new FormatException(
                $"Expected three values after splitting the date string '{dateString}' but there are {splitValue.Length} value(s)");
        }

        string monthString = splitValue[0];
        string dayString = splitValue[1];
        string yearString = splitValue[2];

        if (!int.TryParse(dayString, out int day))
        {
            throw new FormatException($"Could not parse value '{dayString}' for day as integer");
        }

        if (!int.TryParse(monthString, out int month))
        {
            throw new FormatException($"Could not parse value '{monthString}' for month as integer");
        }

        if (!int.TryParse(yearString, out int year))
        {
            throw new FormatException($"Could not parse value '{yearString}' for year as integer");
        }

        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static char[] ParseAllergens(string allergensString)
    {
        // Sample value: "A, C, G"
        return allergensString
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part[0])
            .ToArray();
    }

    private static bool HasNextPageButton(HtmlDocument document)
    {
        return document.DocumentNode.ContainsNode("//a[contains(@class, 'menues-next')]");
    }

    private static bool HasNoMoreOrdersForTodayErrorMessage(HtmlDocument document)
    {
        IEnumerable<HtmlNode> validationNodes = document.DocumentNode.GetNodes("//div[contains(@class, 'validation-message')]");
        return validationNodes.Any(node => node.GetInnerText().Contains("Für heute ist keine Bestellung mehr möglich."));
    }

    private static IEnumerable<GourmetOrderedMenu> ParseOrderedGourmetMenuHtml(HtmlDocument document, bool isOrderChangeForTodayPossible)
    {
        foreach (HtmlNode orderItemNode in document.DocumentNode.GetNodes("//div[contains(@class, 'order-item')]"))
        {
            HtmlNode formNode = orderItemNode.GetSingleNode(".//form[contains(@class, 'form-info-orders')]");
            string positionId = formNode.GetSingleNode(".//input[@name='cp_PositionId']").GetAttributeValue("value");

            HtmlNode eatingCycleIdInputNode = formNode.GetSingleNode($".//input[(@name='cp_EatingCycleId_{positionId}') and @type='hidden']");
            HtmlNode dateInputNode = formNode.GetSingleNode($".//input[(@name='cp_Date_{positionId}') and @type='hidden']");

            string eatingCycleId = eatingCycleIdInputNode.GetAttributeValue("value");
            DateTime day = ParseOrderedMenuDateString(dateInputNode.GetAttributeValue("value"));
            string title = formNode.GetSingleNode(".//div[@class='title']").GetInnerText();
            bool isOrderApproved;

            // This <input> node is only available if the web page is currently in edit mode.
            if (orderItemNode.TryGetSingleNode(
                    $".//input[@name='cec_NewEatingCycleId_{positionId}' and @type='radio']",
                    out HtmlNode? orderApprovedInputNode))
            {
                isOrderApproved = orderApprovedInputNode.GetAttributeValue("class").Contains("confirmed");
            }
            else
            {
                // If the web page is not in edit mode, then this <i> node indicates whether the order is approved.
                isOrderApproved = orderItemNode.ContainsNode(".//span[@class='checkmark']//i[@class='fa fa-check']");
            }

            bool isOrderCancelable = !IsToday(day) || isOrderChangeForTodayPossible;
            yield return new GourmetOrderedMenu(day, positionId, eatingCycleId, title, isOrderApproved, isOrderCancelable);
        }

        bool IsToday(DateTime day)
        {
            Debug.Assert(day.Kind == DateTimeKind.Utc);
            DateTime today = DateTime.UtcNow;
            return day.Day == today.Day && day.Month == today.Month && day.Year == today.Year;
        }
    }

    private static DateTime ParseOrderedMenuDateString(string dateString)
    {
        // Sample value: "30.06.2025 00:00:00"
        string[] spaceSplitValue = dateString.Split(' ');
        if (spaceSplitValue.Length != 2)
        {
            throw new FormatException($"Expected two values after splitting the date node value '{dateString}' but there are {spaceSplitValue.Length} value(s)");
        }

        string[] dateSplitValue = spaceSplitValue[0].Split('.');
        if (dateSplitValue.Length != 3)
        {
            throw new FormatException($"Expected three values after splitting the date node value '{dateString}' but there are {spaceSplitValue.Length} value(s)");
        }

        string dayString = dateSplitValue[0];
        string monthString = dateSplitValue[1];
        string yearString = dateSplitValue[2];

        if (!int.TryParse(dayString, out int day))
        {
            throw new FormatException($"Could not parse value '{dayString}' for day as integer");
        }

        if (!int.TryParse(monthString, out int month))
        {
            throw new FormatException($"Could not parse value '{monthString}' for month as integer");
        }

        if (!int.TryParse(yearString, out int year))
        {
            throw new FormatException($"Could not parse value '{year}' for year as integer");
        }

        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static bool IsOrderedMenuPageEditModeActive(HtmlDocument document)
    {
        HtmlNode toggleEditModeParameterNode = document.DocumentNode
            .GetSingleNode("//form[@id='form_toggleEditMode']//input[@name='editMode' and @type='hidden']");

        // Edit mode is active if value is "False", because when submitting this value would disable the edit mode
        return toggleEditModeParameterNode.GetAttributeValue("value") == "False";
    }

    private static Dictionary<string, string> GetToggleOrderMenuEditModeParameters(HtmlDocument document)
    {
        HtmlNode formNode = document.DocumentNode.GetSingleNode("//form[@id='form_toggleEditMode']");
        HtmlNode editModeNode = formNode.GetSingleNode(".//input[@name='editMode' and @type='hidden']");

        string editModeValue = editModeNode.GetAttributeValue("value");
        string ufprtValue = ParseUfprtValue(formNode);

        return new Dictionary<string, string>
        {
            {"editMode", editModeValue},
            {"ufprt", ufprtValue}
        };
    }

    private static Dictionary<string, string> GetCancelOrderParameters(HtmlDocument document, string positionId)
    {
        string eatingCycleIdNodeName = $"cp_EatingCycleId_{positionId}";
        string dateNodeName = $"cp_Date_{positionId}";

        HtmlNode formNode = document.DocumentNode.GetSingleNode($"//form[@id='form_{positionId}_cp']");
        HtmlNode eatingCycleIdInputNode = formNode.GetSingleNode($".//input[(@name='{eatingCycleIdNodeName}') and @type='hidden']");
        HtmlNode dateInputNode = formNode.GetSingleNode($".//input[(@name='{dateNodeName}') and @type='hidden']");

        string eatingCycleIdValue = eatingCycleIdInputNode.GetAttributeValue("value");
        string dateValue = dateInputNode.GetAttributeValue("value");
        string ufprtValue = ParseUfprtValue(formNode);

        return new Dictionary<string, string>
        {
            {"cp_PositionId", positionId},
            {eatingCycleIdNodeName, eatingCycleIdValue},
            {dateNodeName, dateValue},
            {"ufprt", ufprtValue}
        };
    }

    private static bool IsParseException(Exception exception)
    {
        return exception is GourmetHtmlNodeException || exception is FormatException;
    }
}