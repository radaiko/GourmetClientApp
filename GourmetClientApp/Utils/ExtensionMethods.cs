using HtmlAgilityPack;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using GourmetClientApp.Network;

namespace GourmetClientApp.Utils;

public static class ExtensionMethods
{
    public static HtmlNode GetSingleNode(this HtmlNode node, string xpath)
    {
        return node.SelectSingleNode(xpath) ?? throw new GourmetHtmlNodeException($"No node found for XPath '{xpath}'");
    }
    public static bool TryGetSingleNode(this HtmlNode node, string xpath, [NotNullWhen(true)] out HtmlNode? foundNode)
    {
        foundNode = node.SelectSingleNode(xpath);
        return foundNode is not null;
    }

    public static bool ContainsNode(this HtmlNode node, string xpath)
    {
        return node.SelectSingleNode(xpath) is not null;
    }

    public static IEnumerable<HtmlNode> GetNodes(this HtmlNode node, string xpath)
    {
        var nodes = node.SelectNodes(xpath);
        if (nodes is not null)
        {
            return nodes;
        }

        return [];
    }

    public static string GetInnerText(this HtmlNode node)
    {
        return WebUtility.HtmlDecode(node.InnerText.Trim());
    }

    public static HtmlNode GetChildNodeAtIndex(this HtmlNode node, int index)
    {
        HtmlNodeCollection childNodes = node.ChildNodes;
        if (childNodes.Count < index)
        {
            throw new GourmetHtmlNodeException($"Cannot read child node at index {index} because the parent node has {childNodes.Count} children");
        }

        return childNodes[index];
    }

    public static string GetAttributeValue(this HtmlNode node, string attributeName)
    {
        return node.Attributes[attributeName].Value ?? throw new GourmetHtmlNodeException($"Attribute '{attributeName}' not found on node");
    }

    public static bool TryGetAttributeValue(this HtmlNode node, string attributeName, [NotNullWhen(true)] out string? value)
    {
        value = node.Attributes[attributeName].Value;
        return value is not null;
    }
}