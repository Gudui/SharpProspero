// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Xml;
using System;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class XmlTests
{
    [Fact]
    public void ParsesElementsAttributesAndText()
    {
        var doc = XmlDocument.Parse("<config version=\"2\"><name>Level 1</name><count>3</count></config>");
        Assert.Equal("config", doc.Root.Name);
        Assert.Equal("2", doc.Root.Attribute("version"));
        Assert.Null(doc.Root.Attribute("missing"));
        Assert.Equal("Level 1", doc.Root.Element("name")!.Text);
        Assert.Equal("3", doc.Root.Element("count")!.Text);
    }

    [Fact]
    public void ResolvesEntitiesInTextAndAttributes()
    {
        var doc = XmlDocument.Parse("<n label=\"a &amp; b &#65;&#x42;\">1 &lt; 2 &gt; 0 &quot;q&quot; &apos;a&apos;</n>");
        Assert.Equal("a & b AB", doc.Root.Attribute("label"));
        Assert.Equal("1 < 2 > 0 \"q\" 'a'", doc.Root.Text);
    }

    [Fact]
    public void HandlesCDataAndSelfClosingAndComments()
    {
        var doc = XmlDocument.Parse("<r><!-- note --><raw><![CDATA[ <not> & parsed ]]></raw><empty/></r>");
        Assert.Equal(" <not> & parsed ", doc.Root.Element("raw")!.Text);
        Assert.NotNull(doc.Root.Element("empty"));
        Assert.Empty(doc.Root.Element("empty")!.Children);
    }

    [Fact]
    public void IgnoresDeclarationAndProcessingInstructions()
    {
        var doc = XmlDocument.Parse("<?xml version=\"1.0\" encoding=\"utf-8\"?><?app run?><root>x</root>");
        Assert.Equal("root", doc.Root.Name);
        Assert.Equal("x", doc.Root.Text);
    }

    [Fact]
    public void SkipsDoctypeWithInternalSubset()
    {
        var doc = XmlDocument.Parse("<!DOCTYPE root [ <!ELEMENT root ANY> ]><root/>");
        Assert.Equal("root", doc.Root.Name);
    }

    [Fact]
    public void QueriesElementsAndDescendants()
    {
        var doc = XmlDocument.Parse("<library><book id=\"1\"><tag>a</tag></book><book id=\"2\"><tag>b</tag></book></library>");
        Assert.Equal(2, doc.Root.Elements("book").Count());
        Assert.Equal(["1", "2"], doc.Root.Elements("book").Select(b => b.Attribute("id")!).ToArray());
        Assert.Equal(["a", "b"], doc.Root.Descendants("tag").Select(t => t.Text).ToArray());
    }

    [Fact]
    public void BuildsAndSerializesATree()
    {
        var root = new XmlElement("save").SetAttribute("slot", "1");
        root.AddElement("player").SetAttribute("name", "A & B").Text = "hero";
        root.AddElement("score").Text = "1200";
        string xml = new XmlDocument(root).ToXml();

        Assert.Contains("<save slot=\"1\">", xml);
        Assert.Contains("<player name=\"A &amp; B\">hero</player>", xml);
        Assert.Contains("<score>1200</score>", xml);
    }

    [Fact]
    public void RoundTripsThroughTextPreservingContent()
    {
        var root = new XmlElement("root");
        root.AddElement("a").Text = "less < than & amp";
        root.AddElement("b").SetAttribute("k", "\"v\"").Text = "x";
        var child = root.AddElement("group");
        child.AddElement("item").Text = "one";
        child.AddElement("item").Text = "two";

        string xml = new XmlDocument(root).ToXml(indent: true);
        var reparsed = XmlDocument.Parse(xml);

        Assert.Equal("less < than & amp", reparsed.Root.Element("a")!.Text);
        Assert.Equal("\"v\"", reparsed.Root.Element("b")!.Attribute("k"));
        Assert.Equal(["one", "two"], reparsed.Root.Element("group")!.Elements("item").Select(i => i.Text).ToArray());
    }

    [Fact]
    public void SelfClosesEmptyElementsWhenWriting()
    {
        var root = new XmlElement("a");
        root.AddElement("empty");
        Assert.Contains("<empty />".Replace(" ", ""), new XmlDocument(root).ToXml().Replace(" ", ""));
    }

    [Fact]
    public void IndentsNestedElements()
    {
        var root = new XmlElement("a");
        root.AddElement("b").AddElement("c").Text = "x";
        string xml = new XmlWriter(indent: true).Also(root);
        Assert.Contains("\n  <b>", xml);
        Assert.Contains("\n    <c>x</c>", xml);
    }

    [Theory]
    [InlineData("<a></b>")]                       // mismatched end tag
    [InlineData("<a><b></a></b>")]                // crossed tags
    [InlineData("<a>")]                           // unclosed
    [InlineData("<a/><b/>")]                      // two roots
    [InlineData("<a>&bogus;</a>")]                // unknown entity
    [InlineData("<a b=unquoted>x</a>")]           // unquoted attribute
    [InlineData("<a><!-- unterminated")]          // unterminated comment
    public void RejectsMalformedXml(string xml)
    {
        Assert.Throws<XmlException>(() => XmlDocument.Parse(xml));
    }

    [Fact]
    public void MalformedExceptionReportsPosition()
    {
        var ex = Assert.Throws<XmlException>(() => XmlDocument.Parse("<a>\n  <b></c>\n</a>"));
        Assert.Equal(2, ex.Line);
    }

    [Fact]
    public void RejectsSurrogateNumericCharacterReferences()
    {
        // These pass a naive 0..0x10FFFF range check but are not valid scalar values; they must be
        // reported as XmlException, not leak an ArgumentOutOfRangeException from the decoder.
        Assert.Throws<XmlException>(() => XmlDocument.Parse("<a>&#xD800;</a>"));
        Assert.Throws<XmlException>(() => XmlDocument.Parse("<a>&#55296;</a>"));
        Assert.Throws<XmlException>(() => XmlDocument.Parse("<a>&#xDFFF;</a>"));
        Assert.Equal("A", XmlDocument.Parse("<a>&#x41;</a>").Root.Text); // non-surrogate still decodes
    }

    [Fact]
    public void RejectsExcessivelyDeepNesting()
    {
        string deep = string.Concat(Enumerable.Repeat("<a>", 5000)) + string.Concat(Enumerable.Repeat("</a>", 5000));
        Assert.Throws<XmlException>(() => XmlDocument.Parse(deep));
    }

    [Fact]
    public void PreservesWhitespaceOnlyLeafButNotContainerWhitespace()
    {
        Assert.Equal(" ", XmlDocument.Parse("<a> </a>").Root.Text);        // leaf whitespace kept
        Assert.Equal("", XmlDocument.Parse("<root>\n  <a/>\n</root>").Root.Text); // container whitespace dropped
        Assert.Equal("", XmlDocument.Parse("<root>\n  <a/>\n</root>").Root.Element("a")!.Text);
    }

    [Fact]
    public void EscapesCarriageReturnInTextSoItSurvivesRoundTrip()
    {
        string xml = new XmlDocument(new XmlElement("a") { Text = "one\rtwo" }).ToXml();
        Assert.Contains("&#13;", xml);
        Assert.Equal("one\rtwo", XmlDocument.Parse(xml).Root.Text);
    }

    [Fact]
    public void WriterRejectsInvalidElementAndAttributeNames()
    {
        Assert.Throws<ArgumentException>(() => new XmlWriter().WriteStartElement("bad name"));
        var w = new XmlWriter();
        w.WriteStartElement("ok");
        Assert.Throws<ArgumentException>(() => w.WriteAttribute("a=b", "v"));
    }

    [Fact]
    public void ReaderReportsNodeTypesInOrder()
    {
        var reader = new XmlReader("<r a=\"1\">text<c/></r>");
        var types = new System.Collections.Generic.List<XmlNodeType>();
        while (reader.Read())
            types.Add(reader.NodeType);
        Assert.Equal(
            [XmlNodeType.Element, XmlNodeType.Text, XmlNodeType.Element, XmlNodeType.EndElement],
            types.ToArray());
    }
}

internal static class XmlWriterTestExtensions
{
    // Writes a tree and returns the text, so the indentation test reads cleanly.
    public static string Also(this XmlWriter writer, XmlElement root)
    {
        root.WriteTo(writer);
        return writer.ToString();
    }
}
