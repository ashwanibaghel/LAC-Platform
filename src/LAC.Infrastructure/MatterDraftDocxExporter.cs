using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LAC.Domain;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace LAC.Infrastructure;

public sealed class MatterDraftDocxExporter
{
    private const string DefaultFont = "Calibri";
    private const string DefaultComplexScriptFont = "Nirmala UI";

    public byte[] Export(MatterDraft draft, MatterDraftLayout layout)
    {
        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new WordDocument();

            var isNoting = draft.DraftType == MatterDraftType.Noting;

            // Document settings (mirrored margins for Noting)
            var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
            settingsPart.Settings = isNoting
                ? new Settings(new MirrorMargins())
                : new Settings();
            settingsPart.Settings.Save();

            // Style definitions
            var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = CreateStyleDefinitions();
            stylesPart.Styles.Save();

            // Numbering definitions for lists
            var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
            var numbering = CreateBaseNumberingDefinitions();
            numberingPart.Numbering = numbering;

            var body = new Body();
            var nextNumId = 3; // 1 is bullet, 2 is decimal default

            RenderDraftContent(body, draft.ContentJson, numbering, ref nextNumId);

            // Append section properties at the end of body
            var sectionProperties = CreateSectionProperties(layout, isNoting);
            body.AppendChild(sectionProperties);

            mainPart.Document.AppendChild(body);
            numberingPart.Numbering.Save();
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public static string SanitizeFileName(string? title, string defaultName = "Matter-Draft")
    {
        if (string.IsNullOrWhiteSpace(title)) return $"{defaultName}.docx";

        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '"', '\'', ';', '/', '\\', ':', '*', '?', '<', '>', '|', '\r', '\n', '\t' })
            .Distinct()
            .ToHashSet();

        var sb = new StringBuilder();
        foreach (var c in title)
        {
            sb.Append(invalidChars.Contains(c) ? '_' : c);
        }

        var cleaned = Regex.Replace(sb.ToString(), @"\s+", " ").Trim('.', ' ', '_');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = defaultName;

        if (!cleaned.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            cleaned += ".docx";

        return cleaned;
    }

    private static SectionProperties CreateSectionProperties(MatterDraftLayout layout, bool isNoting)
    {
        var sectionProps = new SectionProperties();

        // 1 mm = 1440 / 25.4 twips (~56.6929 twips)
        var isLandscape = string.Equals(layout.Orientation, "Landscape", StringComparison.OrdinalIgnoreCase);
        var isLegal = string.Equals(layout.PageSize, "Legal", StringComparison.OrdinalIgnoreCase);

        // A4: 210 x 297 mm -> 11906 x 16838 twips
        // Legal: 215.9 x 355.6 mm (8.5 x 14 in) -> 12240 x 20160 twips
        uint baseWidthTwips = isLegal ? 12240U : 11906U;
        uint baseHeightTwips = isLegal ? 20160U : 16838U;

        uint widthTwips = isLandscape ? baseHeightTwips : baseWidthTwips;
        uint heightTwips = isLandscape ? baseWidthTwips : baseHeightTwips;

        var pageSize = new PageSize
        {
            Width = widthTwips,
            Height = heightTwips,
            Orient = isLandscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait
        };
        sectionProps.AppendChild(pageSize);

        int topTwips = (int)Math.Round(layout.MarginTopMm * 1440m / 25.4m);
        int bottomTwips = (int)Math.Round(layout.MarginBottomMm * 1440m / 25.4m);
        int rightTwips = (int)Math.Round(layout.MarginRightMm * 1440m / 25.4m);
        int leftTwips = (int)Math.Round(layout.MarginLeftMm * 1440m / 25.4m);
        uint gutterTwips = 0U;

        if (isNoting)
        {
            // Delhi LAC Noting Sheet: 45 mm mirrored gutter, 25 mm top/bottom, 0 mm left/right
            topTwips = (int)Math.Round(25m * 1440m / 25.4m); // 1417 twips
            bottomTwips = (int)Math.Round(25m * 1440m / 25.4m); // 1417 twips
            leftTwips = 0;
            rightTwips = 0;
            gutterTwips = (uint)Math.Round(45m * 1440m / 25.4m); // 2551 twips
        }

        var pageMargin = new PageMargin
        {
            Top = topTwips,
            Right = (uint)Math.Max(0, rightTwips),
            Bottom = bottomTwips,
            Left = (uint)Math.Max(0, leftTwips),
            Gutter = gutterTwips,
            Header = 720U,
            Footer = 720U
        };
        sectionProps.AppendChild(pageMargin);

        return sectionProps;
    }

    private static Styles CreateStyleDefinitions()
    {
        var styles = new Styles();

        // Normal style
        var normalStyle = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Normal",
            Default = true,
            StyleName = new StyleName { Val = "Normal" },
            PrimaryStyle = new PrimaryStyle(),
            StyleRunProperties = new StyleRunProperties(
                new RunFonts { Ascii = DefaultFont, HighAnsi = DefaultFont, ComplexScript = DefaultComplexScriptFont },
                new Color { Val = "111827" },   // color before sz/szCs per CT_RPr schema order
                new FontSize { Val = "22" },     // 11 pt
                new FontSizeComplexScript { Val = "22" }
            ),
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { After = "120", Line = "240", LineRule = LineSpacingRuleValues.Auto }
            )
        };
        styles.AppendChild(normalStyle);

        // Heading styles 1-6
        var headingDefs = new (int Level, string SizeHalfPt, string SpacingBefore, string SpacingAfter)[]
        {
            (1, "32", "240", "120"), // 16 pt
            (2, "28", "200", "100"), // 14 pt
            (3, "24", "160", "80"),  // 12 pt
            (4, "22", "120", "60"),  // 11 pt
            (5, "20", "100", "50"),  // 10 pt
            (6, "18", "80", "40")    // 9 pt
        };

        foreach (var h in headingDefs)
        {
            var hStyle = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = $"Heading{h.Level}",
                StyleName = new StyleName { Val = $"heading {h.Level}" },
                BasedOn = new BasedOn { Val = "Normal" },
                NextParagraphStyle = new NextParagraphStyle { Val = "Normal" },
                PrimaryStyle = new PrimaryStyle(),
                StyleRunProperties = new StyleRunProperties(
                    new Bold(),
                    new Color { Val = "1F2937" },   // color before sz/szCs per CT_RPr schema order
                    new FontSize { Val = h.SizeHalfPt },
                    new FontSizeComplexScript { Val = h.SizeHalfPt }
                ),
                StyleParagraphProperties = new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = h.SpacingBefore, After = h.SpacingAfter }
                )
            };
            styles.AppendChild(hStyle);
        }

        // TableGrid style
        var tableGridStyle = new Style
        {
            Type = StyleValues.Table,
            StyleId = "TableGrid",
            StyleName = new StyleName { Val = "Table Grid" },
            PrimaryStyle = new PrimaryStyle(),
            StyleTableProperties = new StyleTableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                    new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                    new RightBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" }
                )
            )
        };
        styles.AppendChild(tableGridStyle);

        return styles;
    }

    private static Numbering CreateBaseNumberingDefinitions()
    {
        var numbering = new Numbering();

        // AbstractNum 1: Bullet list
        var bulletAbstract = new AbstractNum
        {
            AbstractNumberId = 1,
            MultiLevelType = new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }
        };
        for (int i = 0; i <= 8; i++)
        {
            var lvl = new Level { LevelIndex = i };
            lvl.AppendChild(new StartNumberingValue { Val = 1 });
            lvl.AppendChild(new NumberingFormat { Val = NumberFormatValues.Bullet });
            lvl.AppendChild(new LevelText { Val = i % 2 == 0 ? "•" : "○" });
            lvl.AppendChild(new LevelJustification { Val = LevelJustificationValues.Left });
            lvl.AppendChild(new ParagraphProperties(
                new Indentation { Left = ((i + 1) * 720).ToString(), Hanging = "360" }
            ));
            bulletAbstract.AppendChild(lvl);
        }
        numbering.AppendChild(bulletAbstract);

        // AbstractNum 2: Decimal list
        var decimalAbstract = new AbstractNum
        {
            AbstractNumberId = 2,
            MultiLevelType = new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }
        };
        for (int i = 0; i <= 8; i++)
        {
            var lvl = new Level { LevelIndex = i };
            lvl.AppendChild(new StartNumberingValue { Val = 1 });
            lvl.AppendChild(new NumberingFormat { Val = NumberFormatValues.Decimal });
            lvl.AppendChild(new LevelText { Val = $"%{i + 1}." });
            lvl.AppendChild(new LevelJustification { Val = LevelJustificationValues.Left });
            lvl.AppendChild(new ParagraphProperties(
                new Indentation { Left = ((i + 1) * 720).ToString(), Hanging = "360" }
            ));
            decimalAbstract.AppendChild(lvl);
        }
        numbering.AppendChild(decimalAbstract);

        // NumberingInstance 1 -> Bullet
        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });

        // NumberingInstance 2 -> Decimal
        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 });

        return numbering;
    }

    private static void RenderDraftContent(Body body, string? contentJson, Numbering numbering, ref int nextNumId)
    {
        // Null/empty contentJson is never stored by the API (validation rejects it),
        // but if it somehow reaches here it is structurally invalid → throw.
        if (string.IsNullOrWhiteSpace(contentJson))
            throw new DocxExportException("Draft content is missing or empty and cannot be exported.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(contentJson);
        }
        catch (JsonException ex)
        {
            throw new DocxExportException("Draft content is not valid JSON and cannot be exported.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var rootType)
                || rootType.GetString() != "doc")
            {
                throw new DocxExportException(
                    "Draft content does not represent a valid editor document and cannot be exported.");
            }

            // Valid document with no content array (or empty content array) → produce one empty paragraph.
            // This is the "valid empty TipTap document" case and MUST export successfully.
            if (root.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in contentArray.EnumerateArray())
                {
                    RenderBlockNode(body, child, numbering, ref nextNumId, listLevel: 0, currentListNumId: null);
                }
            }

            // Ensure body always has at least one paragraph (OpenXml requirement for valid .docx)
            if (!body.Elements<Paragraph>().Any() && !body.Elements<Table>().Any())
            {
                body.AppendChild(new Paragraph());
            }
        }
    }

    private static void RenderBlockNode(
        Body body,
        JsonElement node,
        Numbering numbering,
        ref int nextNumId,
        int listLevel,
        int? currentListNumId)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        var type = node.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        if (string.IsNullOrEmpty(type)) return;

        switch (type)
        {
            case "paragraph":
                body.AppendChild(CreateParagraph(node, numberingProperties: null));
                break;

            case "heading":
                body.AppendChild(CreateHeading(node));
                break;

            case "bulletList":
                RenderList(body, node, numbering, ref nextNumId, listLevel, isOrdered: false);
                break;

            case "orderedList":
                RenderList(body, node, numbering, ref nextNumId, listLevel, isOrdered: true);
                break;

            case "table":
                body.AppendChild(CreateTable(node));
                break;

            case "blockquote":
                body.AppendChild(CreateBlockquote(node));
                break;

            case "horizontalRule":
                body.AppendChild(CreateHorizontalRule());
                break;

            case "draftPage":
                // Legacy draftPage wrapper: recursively render children
                if (node.TryGetProperty("content", out var pageContent) && pageContent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in pageContent.EnumerateArray())
                    {
                        RenderBlockNode(body, child, numbering, ref nextNumId, listLevel, currentListNumId);
                    }
                }
                break;

            default:
                // Unknown block fallback: if content exists, recurse; otherwise extract text
                if (node.TryGetProperty("content", out var fallbackContent) && fallbackContent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in fallbackContent.EnumerateArray())
                    {
                        RenderBlockNode(body, child, numbering, ref nextNumId, listLevel, currentListNumId);
                    }
                }
                else if (node.TryGetProperty("text", out var fallbackText) && fallbackText.ValueKind == JsonValueKind.String)
                {
                    var p = new Paragraph();
                    p.AppendChild(new Run(new Text(fallbackText.GetString()!) { Space = SpaceProcessingModeValues.Preserve }));
                    body.AppendChild(p);
                }
                break;
        }
    }

    private static void RenderList(
        Body body,
        JsonElement listNode,
        Numbering numbering,
        ref int nextNumId,
        int listLevel,
        bool isOrdered)
    {
        int listNumId;
        if (isOrdered)
        {
            listNumId = nextNumId++;
            int startVal = 1;
            if (listNode.TryGetProperty("attrs", out var attrs) &&
                attrs.TryGetProperty("start", out var startEl) &&
                startEl.TryGetInt32(out var parsedStart) && parsedStart > 0)
            {
                startVal = parsedStart;
            }

            var numInstance = new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = listNumId };
            if (startVal != 1)
            {
                numInstance.AppendChild(new LevelOverride(
                    new StartOverrideNumberingValue { Val = startVal }
                ) { LevelIndex = 0 });
            }
            numbering.AppendChild(numInstance);
        }
        else
        {
            listNumId = 1; // standard bullet instance
        }

        if (listNode.TryGetProperty("content", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var itemType = item.TryGetProperty("type", out var it) ? it.GetString() : null;
                if (itemType != "listItem") continue;

                if (item.TryGetProperty("content", out var itemContent) && itemContent.ValueKind == JsonValueKind.Array)
                {
                    bool isFirstPara = true;
                    foreach (var child in itemContent.EnumerateArray())
                    {
                        var childType = child.TryGetProperty("type", out var ct) ? ct.GetString() : null;
                        if (childType is "bulletList" or "orderedList")
                        {
                            RenderList(body, child, numbering, ref nextNumId, listLevel + 1, childType == "orderedList");
                        }
                        else if (childType == "paragraph")
                        {
                            var numProp = isFirstPara
                                ? new NumberingProperties(
                                    new NumberingLevelReference { Val = Math.Clamp(listLevel, 0, 8) },
                                    new NumberingId { Val = listNumId })
                                : null;
                            body.AppendChild(CreateParagraph(child, numProp));
                            isFirstPara = false;
                        }
                        else
                        {
                            RenderBlockNode(body, child, numbering, ref nextNumId, listLevel, listNumId);
                        }
                    }
                }
            }
        }
    }

    private static Paragraph CreateParagraph(JsonElement node, NumberingProperties? numberingProperties)
    {
        var paragraph = new Paragraph();
        var pPr = new ParagraphProperties();

        if (numberingProperties != null)
        {
            pPr.AppendChild(numberingProperties);
        }

        pPr.AppendChild(new SpacingBetweenLines { After = "120", Line = "240", LineRule = LineSpacingRuleValues.Auto });

        if (node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            if (attrs.TryGetProperty("textAlign", out var alignEl))
            {
                var jc = MapJustification(alignEl.GetString());
                if (jc != null) pPr.AppendChild(jc);
            }
        }

        paragraph.AppendChild(pPr);
        AppendInlines(paragraph, node);

        return paragraph;
    }

    private static Paragraph CreateHeading(JsonElement node)
    {
        var paragraph = new Paragraph();
        var pPr = new ParagraphProperties();

        int level = 1;
        if (node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            if (attrs.TryGetProperty("level", out var lvlEl) && lvlEl.TryGetInt32(out var l) && l >= 1 && l <= 6)
            {
                level = l;
            }
        }

        pPr.AppendChild(new ParagraphStyleId { Val = $"Heading{level}" });

        if (attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("textAlign", out var alignEl))
        {
            var jc = MapJustification(alignEl.GetString());
            if (jc != null) pPr.AppendChild(jc);
        }

        paragraph.AppendChild(pPr);
        AppendInlines(paragraph, node);

        return paragraph;
    }

    private static Paragraph CreateBlockquote(JsonElement node)
    {
        var paragraph = new Paragraph();
        var pPr = new ParagraphProperties();

        pPr.AppendChild(new ParagraphBorders(
            new LeftBorder { Val = BorderValues.Single, Size = 24, Color = "9CA3AF", Space = 8 }
        ));
        pPr.AppendChild(new SpacingBetweenLines { Before = "120", After = "120", Line = "240", LineRule = LineSpacingRuleValues.Auto });
        pPr.AppendChild(new Indentation { Left = "720" });

        paragraph.AppendChild(pPr);

        // If blockquote has nested paragraph(s), extract their inlines
        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                AppendInlines(paragraph, child, defaultItalic: true);
            }
        }
        else
        {
            AppendInlines(paragraph, node, defaultItalic: true);
        }

        return paragraph;
    }

    private static Paragraph CreateHorizontalRule()
    {
        var paragraph = new Paragraph();
        var pPr = new ParagraphProperties(
            new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 12, Color = "D1D5DB", Space = 1 }),
            new SpacingBetweenLines { Before = "120", After = "120" }
        );
        paragraph.AppendChild(pPr);
        return paragraph;
    }

    private static Table CreateTable(JsonElement tableNode)
    {
        var table = new Table();

        var tblPr = new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "D1D5DB" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" }
            )
        );
        table.AppendChild(tblPr);

        // OOXML requires w:tblGrid immediately after w:tblPr and before any rows.
        // Determine column count from the first row to build evenly-spaced grid columns.
        int colCount = 1;
        if (tableNode.TryGetProperty("content", out var rowsForGrid) && rowsForGrid.ValueKind == JsonValueKind.Array)
        {
            foreach (var firstRow in rowsForGrid.EnumerateArray())
            {
                if (firstRow.ValueKind == JsonValueKind.Object
                    && firstRow.TryGetProperty("content", out var firstCells)
                    && firstCells.ValueKind == JsonValueKind.Array)
                {
                    // Count cells, accounting for colspan attrs
                    int count = 0;
                    foreach (var cell in firstCells.EnumerateArray())
                    {
                        int span = 1;
                        if (cell.TryGetProperty("attrs", out var a) && a.TryGetProperty("colspan", out var cs) && cs.TryGetInt32(out var csVal) && csVal > 1)
                            span = csVal;
                        count += span;
                    }
                    if (count > 0) colCount = count;
                }
                break; // only need first row
            }
        }

        // Each grid column gets an equal share of 5000 (hundredths of a percent of page width)
        // Use twips-based width: 9638 twips = ~170mm usable width for A4/Legal; distribute evenly
        int colWidthTwips = 9638 / colCount;
        var tblGrid = new TableGrid();
        for (int c = 0; c < colCount; c++)
            tblGrid.AppendChild(new GridColumn { Width = colWidthTwips.ToString() });
        table.AppendChild(tblGrid);

        if (tableNode.TryGetProperty("content", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var rowNode in rows.EnumerateArray())
            {
                if (rowNode.ValueKind != JsonValueKind.Object) continue;
                var rowType = rowNode.TryGetProperty("type", out var rt) ? rt.GetString() : null;
                if (rowType != "tableRow") continue;

                var tableRow = new TableRow();

                if (rowNode.TryGetProperty("content", out var cells) && cells.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cellNode in cells.EnumerateArray())
                    {
                        if (cellNode.ValueKind != JsonValueKind.Object) continue;
                        var cellType = cellNode.TryGetProperty("type", out var ct) ? ct.GetString() : null;
                        var isHeader = cellType == "tableHeader";

                        var tableCell = new TableCell();
                        var tcPr = new TableCellProperties();

                        int colspan = 1;
                        if (cellNode.TryGetProperty("attrs", out var cellAttrs) && cellAttrs.ValueKind == JsonValueKind.Object)
                        {
                            if (cellAttrs.TryGetProperty("colspan", out var csEl) && csEl.TryGetInt32(out var cs) && cs > 1)
                            {
                                colspan = cs;
                                tcPr.AppendChild(new GridSpan { Val = colspan });
                            }
                        }

                        if (isHeader)
                        {
                            tcPr.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F3F4F6" });
                        }

                        tableCell.AppendChild(tcPr);

                        // Cell content
                        bool hasBlock = false;
                        if (cellNode.TryGetProperty("content", out var cellContent) && cellContent.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var child in cellContent.EnumerateArray())
                            {
                                var childType = child.TryGetProperty("type", out var childTypeEl) ? childTypeEl.GetString() : null;
                                if (childType == "paragraph")
                                {
                                    tableCell.AppendChild(CreateParagraph(child, numberingProperties: null));
                                    hasBlock = true;
                                }
                                else if (childType == "heading")
                                {
                                    tableCell.AppendChild(CreateHeading(child));
                                    hasBlock = true;
                                }
                            }
                        }

                        // Every cell MUST have at least one Paragraph in OpenXml
                        if (!hasBlock)
                        {
                            tableCell.AppendChild(new Paragraph());
                        }

                        tableRow.AppendChild(tableCell);
                    }
                }

                table.AppendChild(tableRow);
            }
        }

        return table;
    }

    private static void AppendInlines(Paragraph paragraph, JsonElement parentNode, bool defaultItalic = false)
    {
        if (!parentNode.TryGetProperty("content", out var inlines) || inlines.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var inline in inlines.EnumerateArray())
        {
            if (inline.ValueKind != JsonValueKind.Object) continue;
            var type = inline.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "text")
            {
                var text = inline.TryGetProperty("text", out var textEl) ? textEl.GetString() : "";
                if (string.IsNullOrEmpty(text)) continue;

                var run = CreateRun(text, inline, defaultItalic);
                paragraph.AppendChild(run);
            }
            else if (type == "hardBreak")
            {
                paragraph.AppendChild(new Run(new Break()));
            }
            else
            {
                // Fallback for unknown inline
                if (inline.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                {
                    var run = CreateRun(textEl.GetString()!, inline, defaultItalic);
                    paragraph.AppendChild(run);
                }
            }
        }
    }

    private static Run CreateRun(string text, JsonElement textNode, bool defaultItalic)
    {
        var run = new Run();
        var rPr = new RunProperties();

        bool hasBold = false;
        bool hasItalic = defaultItalic;
        bool hasUnderline = false;
        bool hasStrike = false;
        string? fontFamily = null;
        string? fontColor = null;
        string? fontSize = null;

        if (textNode.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
        {
            foreach (var mark in marks.EnumerateArray())
            {
                if (mark.ValueKind != JsonValueKind.Object) continue;
                var markType = mark.TryGetProperty("type", out var mt) ? mt.GetString() : null;
                switch (markType)
                {
                    case "bold":
                        hasBold = true;
                        break;
                    case "italic":
                        hasItalic = true;
                        break;
                    case "underline":
                        hasUnderline = true;
                        break;
                    case "strike":
                        hasStrike = true;
                        break;
                    case "textStyle":
                        if (mark.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                        {
                            if (attrs.TryGetProperty("fontFamily", out var ff) && ff.ValueKind == JsonValueKind.String)
                                fontFamily = ff.GetString();
                            if (attrs.TryGetProperty("color", out var col) && col.ValueKind == JsonValueKind.String)
                                fontColor = col.GetString();
                            if (attrs.TryGetProperty("fontSize", out var fs) && fs.ValueKind == JsonValueKind.String)
                                fontSize = fs.GetString();
                        }
                        break;
                }
            }
        }

        // Schema order for RunProperties child elements:
        // 1. RunFonts
        // 2. Bold
        // 3. Italic
        // 4. Strike
        // 5. Color
        // 6. FontSize
        // 7. FontSizeComplexScript
        // 8. Underline

        var primaryFont = !string.IsNullOrWhiteSpace(fontFamily) ? fontFamily : DefaultFont;
        rPr.AppendChild(new RunFonts
        {
            Ascii = primaryFont,
            HighAnsi = primaryFont,
            ComplexScript = !string.IsNullOrWhiteSpace(fontFamily) ? fontFamily : DefaultComplexScriptFont
        });

        if (hasBold) rPr.AppendChild(new Bold());
        if (hasItalic) rPr.AppendChild(new Italic());
        if (hasStrike) rPr.AppendChild(new Strike());

        if (!string.IsNullOrWhiteSpace(fontColor))
        {
            var hex = fontColor.TrimStart('#');
            if (hex.Length >= 6)
            {
                rPr.AppendChild(new Color { Val = hex[..6] });
            }
        }

        if (!string.IsNullOrWhiteSpace(fontSize))
        {
            int halfPoints = ParseFontSizeToHalfPoints(fontSize);
            if (halfPoints > 0)
            {
                rPr.AppendChild(new FontSize { Val = halfPoints.ToString() });
                rPr.AppendChild(new FontSizeComplexScript { Val = halfPoints.ToString() });
            }
        }

        if (hasUnderline)
        {
            rPr.AppendChild(new Underline { Val = UnderlineValues.Single });
        }

        run.AppendChild(rPr);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        return run;
    }

    private static int ParseFontSizeToHalfPoints(string fontSize)
    {
        fontSize = fontSize.Trim();
        if (fontSize.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            if (decimal.TryParse(fontSize[..^2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var px))
                return (int)Math.Round(px * 1.5m);
        }
        else if (fontSize.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            if (decimal.TryParse(fontSize[..^2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pt))
                return (int)Math.Round(pt * 2m);
        }
        else if (decimal.TryParse(fontSize, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num))
        {
            return (int)Math.Round(num * 2m);
        }
        return 0;
    }

    private static Justification? MapJustification(string? align)
    {
        return align?.ToLowerInvariant() switch
        {
            "center" => new Justification { Val = JustificationValues.Center },
            "right" => new Justification { Val = JustificationValues.Right },
            "justify" => new Justification { Val = JustificationValues.Both },
            "left" => new Justification { Val = JustificationValues.Left },
            _ => null
        };
    }
}

/// <summary>
/// Thrown when a MatterDraft's stored content cannot be safely exported to DOCX.
/// The message is safe to surface in a ProblemDetails response without exposing
/// internal implementation details.
/// </summary>
public sealed class DocxExportException : Exception
{
    public DocxExportException(string message) : base(message) { }
    public DocxExportException(string message, Exception inner) : base(message, inner) { }
}
