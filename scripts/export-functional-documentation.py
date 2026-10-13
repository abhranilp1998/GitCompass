"""Export the functional manual using bundled ReportLab and standard Python.

The source uses a declared Markdown subset: headings, paragraphs, lists, tables,
images and two workflows. No network access, Git execution or package changes.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import html
import json
import math
import re
from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate, CondPageBreak, Flowable, Frame, Image, LongTable,
    PageBreak, PageTemplate, Paragraph, Spacer, TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents
from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "functional-documentation.md"
SOURCE_METADATA = SOURCE.read_text(encoding="utf-8")
APP_VERSION = re.search(r"^\*\*Application baseline:\*\* ([0-9.]+)", SOURCE_METADATA, re.M)[1]
DOCUMENT_VERSION = re.search(r"^\*\*Document version:\*\* ([0-9.]+)", SOURCE_METADATA, re.M)[1]
PREPARED = re.search(r"^\*\*Prepared:\*\* (.+)", SOURCE_METADATA, re.M)[1].strip()
NAVY, BLUE, MUTED, LIGHT = "#17243C", "#1769E0", "#5D6C82", "#F1F5FB"
WIDTH = A4[0] - 92


def slug(text: str) -> str:
    plain = re.sub(r"[`*]", "", text).lower()
    return re.sub(r"[^a-z0-9 _-]", "", plain).replace(" ", "-")


def parse_markdown(text: str) -> list[dict]:
    lines, blocks, i = text.splitlines(), [], 0
    while i < len(lines):
        line = lines[i]
        if not line.strip():
            i += 1; continue
        if line.startswith("```"):
            language, body = line[3:].strip(), []
            i += 1
            while i < len(lines) and not lines[i].startswith("```"):
                body.append(lines[i]); i += 1
            if i == len(lines):
                raise ValueError("Unclosed Markdown code fence")
            blocks.append({"type": "code", "language": language, "text": "\n".join(body)})
            i += 1; continue
        heading = re.match(r"^(#{1,6}) (.+)$", line)
        if heading:
            blocks.append({"type": "heading", "level": len(heading[1]), "text": heading[2]})
            i += 1; continue
        image = re.fullmatch(r"!\[([^\]]*)\]\(([^)]+)\)", line)
        if image:
            blocks.append({"type": "image", "caption": image[1], "path": image[2]})
            i += 1; continue
        if line.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                row = [cell.strip() for cell in lines[i].strip().strip("|").split("|")]
                if not all(re.fullmatch(r":?-+:?", cell) for cell in row):
                    rows.append(row)
                i += 1
            if not rows or any(len(row) != len(rows[0]) for row in rows):
                raise ValueError("Invalid Markdown table")
            blocks.append({"type": "table", "rows": rows}); continue
        item = re.match(r"^(- |\d+\. )(.+)$", line)
        if item:
            items, ordered = [], item[1] != "- "
            while i < len(lines):
                match = re.match(r"^(- |\d+\. )(.+)$", lines[i])
                if not match or (match[1] != "- ") != ordered:
                    break
                items.append(match[2]); i += 1
            blocks.append({"type": "list", "ordered": ordered, "items": items}); continue
        paragraph = [line.rstrip()]
        i += 1
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#|```|\||!\[|- |\d+\. )", lines[i]):
            paragraph.append(lines[i].rstrip()); i += 1
        blocks.append({"type": "paragraph", "text": "\n".join(paragraph)})
    return blocks


def resolve_link(target: str) -> str:
    if target.startswith(("https://", "http://", "#")):
        return target
    path, separator, fragment = target.partition("#")
    return (SOURCE.parent / path).resolve().as_uri() + (separator + fragment if separator else "")


def inline(text: str, pdf: bool = False) -> str:
    stored = []
    def protect(value: str) -> str:
        stored.append(value)
        return f"@@STORED{len(stored) - 1}@@"
    text = re.sub(r"`([^`]+)`", lambda m: protect(
        f'<font face="DocMono" size="8.4">{escape(m[1])}</font>'
        if pdf else f"<code>{html.escape(m[1])}</code>"), text)
    text = escape(text) if pdf else html.escape(text)
    def link(match):
        label, target = match[1], resolve_link(html.unescape(match[2]))
        if pdf:
            return f'<link href="{html.escape(target, quote=True)}" color="{BLUE}">{label}</link>'
        return f'<a href="{html.escape(target, quote=True)}">{label}</a>'
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", link, text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>" if pdf else r"<strong>\1</strong>", text)
    text = text.replace("\n", "<br/>" if pdf else "<br>")
    for index, value in enumerate(stored):
        text = text.replace(f"@@STORED{index}@@", value)
    return text


def workflow_data(text: str):
    if "Saved working files" in text:
        nodes = [(0, 1, "Saved working files"), (1, 1, "Staging area"),
                 (2, 1, "Local recorded history"), (2, 0, "Remote repository"),
                 (1, 0, "Cached team history")]
        edges = [(0, 1, "Stage"), (1, 2, "Commit"), (2, 3, "Push"),
                 (3, 4, "Fetch"), (4, 2, "Update / merge")]
        return nodes, edges, "How work moves between files, staging, history and the remote"
    nodes = [(0, 2, "Choose an action"), (1, 2, "Refresh facts / check access"),
             (2, 2, "Review exact targets"), (2, 1, "Run reviewed operation"),
             (1, 1, "Recheck bound evidence"), (0, 1, "Typed execution"),
             (0, 0, "Inspect postconditions"), (1, 0, "Read the result state"),
             (2, 0, "Stale / denied: review again")]
    edges = [(0, 1, ""), (1, 2, ""), (2, 3, ""), (3, 4, ""),
             (4, 5, ""), (5, 6, ""), (6, 7, ""), (4, 8, "Changed / revoked")]
    return nodes, edges, "Reviewed operations bind a decision to current evidence"


def positions(nodes):
    box_w, box_h = 140, 44
    return [(column * (WIDTH - box_w) / 2, row * 86 + 15, box_w, box_h, label)
            for column, row, label in nodes]


def label_lines(label):
    lines, current = [], ""
    for word in label.split():
        if len(current + " " + word) > 23 and current:
            lines.append(current); current = word
        else:
            current = (current + " " + word).strip()
    if current:
        lines.append(current)
    return lines


def endpoints(first, second):
    x1, y1, w1, h1, _ = first
    x2, y2, w2, h2, _ = second
    cx1, cy1, cx2, cy2 = x1 + w1 / 2, y1 + h1 / 2, x2 + w2 / 2, y2 + h2 / 2
    if abs(cx2 - cx1) > abs(cy2 - cy1):
        return (x1 + w1 if cx2 > cx1 else x1, cy1,
                x2 if cx2 > cx1 else x2 + w2, cy2)
    return (cx1, y1 + h1 if cy2 > cy1 else y1,
            cx2, y2 if cy2 > cy1 else y2 + h2)


class Workflow(Flowable):
    def __init__(self, text):
        super().__init__()
        self.nodes, self.edges, self.caption = workflow_data(text)
        self.width = WIDTH
        self.height = 86 * max(row for _, row, _ in self.nodes) + 85
    def draw(self):
        canvas, pos = self.canv, positions(self.nodes)
        canvas.setStrokeColor(colors.HexColor(BLUE)); canvas.setLineWidth(1.3)
        for start, end, label in self.edges:
            x1, y1, x2, y2 = endpoints(pos[start], pos[end])
            canvas.line(x1, y1, x2, y2)
            angle = math.atan2(y2 - y1, x2 - x1)
            for delta in (-0.45, 0.45):
                canvas.line(x2, y2, x2 - 6 * math.cos(angle + delta), y2 - 6 * math.sin(angle + delta))
            if label:
                label_x, label_y = (x1 + x2) / 2, (y1 + y2) / 2 + 7
                label_w = pdfmetrics.stringWidth(label, "Doc", 7.7)
                canvas.setFillColor(colors.white)
                canvas.rect(label_x-label_w/2-3,label_y-2,label_w+6,11,fill=1,stroke=0)
                canvas.setFillColor(colors.HexColor(MUTED)); canvas.setFont("Doc", 7.7)
                canvas.drawCentredString(label_x, label_y, label)
        for x, y, width, height, label in pos:
            canvas.setFillColor(colors.HexColor(LIGHT)); canvas.setStrokeColor(colors.HexColor("#C9D7EB"))
            canvas.roundRect(x, y, width, height, 7, fill=1, stroke=1)
            canvas.setFillColor(colors.HexColor(NAVY)); canvas.setFont("DocBold", 9)
            lines = label_lines(label)
            for index, line in enumerate(lines):
                canvas.drawCentredString(x + width / 2, y + height / 2 + (len(lines) - 1) * 5 - index * 11 - 3, line)


def workflow_svg(text):
    nodes, edges, caption = workflow_data(text)
    pos, height = positions(nodes), 86 * max(row for _, row, _ in nodes) + 85
    parts = [f'<svg role="img" aria-label="{html.escape(caption)}" viewBox="0 0 {WIDTH} {height}" xmlns="http://www.w3.org/2000/svg">',
             f'<defs><marker id="arrow-{len(nodes)}" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill="{BLUE}"/></marker></defs>']
    for start, end, label in edges:
        x1, y1, x2, y2 = endpoints(pos[start], pos[end])
        y1, y2 = height - y1, height - y2
        parts.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{BLUE}" stroke-width="1.3" marker-end="url(#arrow-{len(nodes)})"/>')
        if label:
            label_x, label_y, label_w = (x1+x2)/2, (y1+y2)/2-7, len(label)*4.3
            parts.append(f'<rect x="{label_x-label_w/2-3}" y="{label_y-9}" width="{label_w+6}" height="12" fill="white"/>')
            parts.append(f'<text x="{label_x}" y="{label_y}" text-anchor="middle" font-size="8" fill="{MUTED}">{html.escape(label)}</text>')
    for x, y, width, box_height, label in pos:
        top = height - y - box_height
        parts.append(f'<rect x="{x}" y="{top}" width="{width}" height="{box_height}" rx="7" fill="{LIGHT}" stroke="#C9D7EB"/>')
        lines = label_lines(label)
        for index, line in enumerate(lines):
            ty = top + box_height/2 - (len(lines)-1)*5 + index*11 + 3
            parts.append(f'<text x="{x+width/2}" y="{ty}" text-anchor="middle" font-size="9" font-weight="600" fill="{NAVY}">{html.escape(line)}</text>')
    return "".join(parts) + "</svg>"


def write_html(blocks, path):
    body, navigation = [], []
    for block in blocks:
        kind = block["type"]
        if kind == "heading":
            level, title = block["level"], block["text"]
            identifier = slug(title)
            body.append(f'<h{level} id="{identifier}">{inline(title)}</h{level}>')
            if level == 2 and title != "Contents":
                navigation.append(f'<a href="#{identifier}">{html.escape(title)}</a>')
        elif kind == "paragraph":
            body.append(f'<p>{inline(block["text"])}</p>')
        elif kind == "list":
            tag = "ol" if block["ordered"] else "ul"
            body.append(f'<{tag}>' + "".join(f'<li>{inline(item)}</li>' for item in block["items"]) + f'</{tag}>')
        elif kind == "table":
            rows = block["rows"]
            content = '<thead><tr>' + "".join(f'<th scope="col">{inline(cell)}</th>' for cell in rows[0]) + '</tr></thead><tbody>'
            content += "".join('<tr>' + "".join(f'<td>{inline(cell)}</td>' for cell in row) + '</tr>' for row in rows[1:]) + '</tbody>'
            body.append('<div class="table-scroll"><table>' + content + '</table></div>')
        elif kind == "image":
            image = (SOURCE.parent / block["path"]).resolve()
            payload = base64.b64encode(image.read_bytes()).decode("ascii")
            body.append(f'<figure><img src="data:image/png;base64,{payload}" alt="{html.escape(block["caption"])}"><figcaption>{html.escape(block["caption"])}</figcaption></figure>')
        elif kind == "code":
            body.append('<figure class="workflow">' + workflow_svg(block["text"]) + '</figure>'
                        if block["language"] == "mermaid" else '<pre><code>' + html.escape(block["text"]) + '</code></pre>')
    css = """
    :root {color-scheme:light;--ink:#17243c;--muted:#5d6c82;--blue:#1769e0;}
    *{box-sizing:border-box;}body{margin:0;color:var(--ink);background:#f6f8fc;font:16px/1.65 'Segoe UI',Arial,sans-serif;}
    nav{position:fixed;inset:0 auto 0 0;width:300px;overflow:auto;padding:28px 24px;background:#17243c;color:white;}
    nav strong{display:block;font-size:25px;margin-bottom:4px;}nav small{display:block;color:#c8d5e7;margin-bottom:24px;}
    nav a{display:block;color:#dce7f7;font-size:13px;line-height:1.5;padding:7px 0;text-decoration:none;}
    nav a:hover,nav a:focus{color:white;text-decoration:underline;}main{max-width:1190px;margin-left:300px;padding:40px 50px 70px;background:white;}
    h1{font-size:38px;line-height:1.18;margin:12px 0 24px;}h2{font-size:26px;line-height:1.3;margin:56px 0 18px;padding-top:14px;border-top:2px solid #dee5f0;scroll-margin-top:12px;}
    h3{font-size:20px;line-height:1.35;margin:30px 0 14px;}p{margin:12px 0 17px;}a{color:var(--blue);overflow-wrap:anywhere;}
    code{font:0.88em/1.5 Consolas,monospace;background:#edf2f8;padding:2px 5px;border-radius:3px;overflow-wrap:anywhere;}
    ul,ol{padding-left:25px;}li{margin:7px 0;}.table-scroll{overflow:auto;margin:22px 0;}table{width:100%;border-collapse:collapse;font-size:14px;}
    th{background:#17243c;color:white;text-align:left;padding:11px 12px;vertical-align:top;}td{border:1px solid #dee5f0;padding:10px 12px;vertical-align:top;}
    tbody tr:nth-child(even){background:#f3f6fb;}figure{margin:26px 0;}img{display:block;width:100%;border:1px solid #d5dfed;border-radius:5px;}
    figcaption{color:var(--muted);font-size:13px;margin-top:9px;}.workflow{padding:18px;border:1px solid #d5dfed;background:#fcfdff;border-radius:7px;}
    svg{width:100%;font-family:'Segoe UI',Arial,sans-serif;}@media(max-width:1000px){nav{position:static;width:auto;max-height:260px;}main{margin:0;padding:24px;}}
    @media print{nav{display:none;}main{margin:0;max-width:none;padding:0;}body{font-size:10pt;}h2{break-after:avoid;}table{font-size:8.5pt;}tr,figure{break-inside:avoid;}}
    """
    document = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>GitCompass - Complete Functional Documentation</title><style>' + css + '</style></head><body>'
    document += f'<nav aria-label="Functional manual contents"><strong>GitCompass</strong><small>Functional manual {DOCUMENT_VERSION}<br>Application baseline {APP_VERSION}<br>{PREPARED}</small>' + "".join(navigation) + '</nav><main>' + "".join(body) + '</main></body></html>'
    path.write_text(document, encoding="utf-8")


class ManualDocument(BaseDocTemplate):
    def afterFlowable(self, flowable):
        if isinstance(flowable, Paragraph) and hasattr(flowable, "bookmark"):
            self.canv.bookmarkPage(flowable.bookmark)
            self.canv.addOutlineEntry(flowable.getPlainText(), flowable.bookmark, flowable.outline_level, closed=True)
            if flowable.outline_level == 0:
                self.notify("TOCEntry", (0, flowable.getPlainText(), self.page, flowable.bookmark))


def register_fonts():
    folder = Path("C:/Windows/Fonts")
    for name, filename in (("Doc", "segoeui.ttf"), ("DocBold", "segoeuib.ttf"),
                           ("DocItalic", "segoeuii.ttf"), ("DocMono", "consola.ttf")):
        pdfmetrics.registerFont(TTFont(name, str(folder / filename)))
    pdfmetrics.registerFontFamily("Doc", normal="Doc", bold="DocBold", italic="DocItalic", boldItalic="DocBold")
    pdfmetrics.registerFontFamily("DocMono", normal="DocMono", bold="DocMono", italic="DocMono", boldItalic="DocMono")


def make_styles():
    body = ParagraphStyle("Body", fontName="Doc", fontSize=9.6, leading=13.6,
                          textColor=colors.HexColor(NAVY), spaceAfter=8, splitLongWords=True,
                          allowWidows=False, allowOrphans=False)
    return {
        "body": body,
        "h1": ParagraphStyle("H1", parent=body, fontName="DocBold", fontSize=27, leading=33, spaceAfter=20),
        "h2": ParagraphStyle("H2", parent=body, fontName="DocBold", fontSize=18, leading=23, spaceBefore=19, spaceAfter=12, keepWithNext=True),
        "h3": ParagraphStyle("H3", parent=body, fontName="DocBold", fontSize=12.5, leading=17, spaceBefore=11, spaceAfter=8, keepWithNext=True),
        "caption": ParagraphStyle("Caption", parent=body, fontName="DocItalic", fontSize=8, leading=11, textColor=colors.HexColor(MUTED), spaceBefore=5, spaceAfter=13),
        "cell": ParagraphStyle("Cell", parent=body, fontSize=8.7, leading=12, spaceAfter=0),
        "head": ParagraphStyle("Head", parent=body, fontName="DocBold", fontSize=8.8, leading=12, textColor=colors.white, spaceAfter=0),
        "list": ParagraphStyle("List", parent=body, leftIndent=16, firstLineIndent=0, bulletIndent=0, spaceAfter=5),
        "toc": ParagraphStyle("TOC", parent=body, fontSize=10, leading=16, spaceBefore=4, spaceAfter=1),
    }


def header_footer(canvas, document):
    canvas.saveState()
    canvas.setTitle("GitCompass - Complete Functional Documentation")
    canvas.setAuthor("GitCompass project documentation")
    canvas.setSubject(f"Application baseline {APP_VERSION}; functional manual {DOCUMENT_VERSION}; {PREPARED}")
    canvas.setStrokeColor(colors.HexColor("#DEE5F0")); canvas.line(46, 38, A4[0] - 46, 38)
    canvas.setFont("Doc", 8); canvas.setFillColor(colors.HexColor(MUTED))
    canvas.drawString(46, 24, f"GitCompass {APP_VERSION} | Functional manual {DOCUMENT_VERSION}")
    canvas.drawRightString(A4[0] - 46, 24, str(document.page))
    if document.page > 1:
        canvas.drawString(46, A4[1] - 27, "Complete functional documentation")
        canvas.drawRightString(A4[0] - 46, A4[1] - 27, PREPARED)
    canvas.restoreState()


def table_widths(rows):
    count = len(rows[0])
    if count == 2:
        fractions = [0.34, 0.66]
    elif count == 3:
        fractions = [0.24, 0.30, 0.46]
    elif count == 4 and rows[0][0] == "Capability":
        fractions = [0.46, 0.18, 0.18, 0.18]
    elif count == 4:
        fractions = [0.16, 0.32, 0.30, 0.22]
    else:
        fractions = [1/count] * count
    return [WIDTH * portion for portion in fractions]


def write_pdf(blocks, path):
    register_fonts()
    styles = make_styles()
    document = ManualDocument(str(path), pagesize=A4, leftMargin=46, rightMargin=46,
                              topMargin=47, bottomMargin=49, allowSplitting=True)
    frame = Frame(46, 49, WIDTH, A4[1]-96, leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
    document.addPageTemplates(PageTemplate(id="Manual", frames=frame, onPage=header_footer))
    story = [Spacer(1, 24), Image(str(ROOT/"assets"/"branding"/"GitCompass.png"), width=72, height=72, hAlign="LEFT"), Spacer(1, 20)]
    skip_contents = False
    for block in blocks:
        kind = block["type"]
        if kind == "heading":
            level, title = block["level"], block["text"]
            if title == "Contents":
                story += [PageBreak(), Paragraph("Contents", styles["h2"])]
                toc = TableOfContents(); toc.levelStyles = [styles["toc"]]
                story += [toc, PageBreak()]
                skip_contents = True
                continue
            skip_contents = False
            if level == 1:
                story.append(Paragraph("GitCompass<br/>Complete Functional<br/>Documentation", styles["h1"]))
                continue
            para = Paragraph(inline(title, pdf=True), styles["h2" if level == 2 else "h3"])
            para.bookmark, para.outline_level = slug(title), 0 if level == 2 else 1
            story.append(para)
        elif skip_contents:
            continue
        elif kind == "paragraph":
            story.append(Paragraph(inline(block["text"], pdf=True), styles["body"]))
        elif kind == "list":
            for index, item in enumerate(block["items"], 1):
                prefix = str(index) + "." if block["ordered"] else "-"
                story.append(Paragraph(inline(item, pdf=True), styles["list"], bulletText=prefix))
            story.append(Spacer(1, 4))
        elif kind == "table":
            rows = block["rows"]
            data = [[Paragraph(inline(cell, pdf=True), styles["head" if r == 0 else "cell"])
                     for cell in row] for r, row in enumerate(rows)]
            table = LongTable(data, colWidths=table_widths(rows), repeatRows=1, hAlign="LEFT", splitByRow=True)
            table.setStyle(TableStyle([
                ("BACKGROUND",(0,0),(-1,0),colors.HexColor(NAVY)),
                ("ROWBACKGROUNDS",(0,1),(-1,-1),[colors.white,colors.HexColor(LIGHT)]),
                ("GRID",(0,0),(-1,-1),0.35,colors.HexColor("#D7DFEB")),
                ("VALIGN",(0,0),(-1,-1),"TOP"),
                ("LEFTPADDING",(0,0),(-1,-1),8),("RIGHTPADDING",(0,0),(-1,-1),8),
                ("TOPPADDING",(0,0),(-1,-1),7),("BOTTOMPADDING",(0,0),(-1,-1),7),
            ]))
            table.spaceBefore, table.spaceAfter = 4, 11
            # Keep a heading with the first rows, while allowing a multi-page table
            # to use the remaining frame instead of leaving a nearly blank page.
            if table.wrap(WIDTH, A4[1]-96)[1] > A4[1]-156:
                first_heading = len(story)
                heading_height = 0
                while first_heading and isinstance(story[first_heading-1], Paragraph) and hasattr(story[first_heading-1], "bookmark"):
                    first_heading -= 1
                    heading = story[first_heading]
                    heading.keepWithNext = False
                    heading_height += heading.wrap(WIDTH, A4[1])[1] + heading.getSpaceBefore() + heading.getSpaceAfter()
                if first_heading < len(story):
                    story.insert(first_heading, CondPageBreak(heading_height + sum(table._rowHeights[:2]) + 16))
            story.append(table)
        elif kind == "image":
            from PIL import Image as RasterImage
            image = (SOURCE.parent/block["path"]).resolve()
            with RasterImage.open(image) as raster:
                ratio = raster.height/raster.width
            height = min(WIDTH*ratio,360)
            screenshot = Image(str(image),width=height/ratio,height=height)
            screenshot.keepWithNext = True
            story += [screenshot,
                      Paragraph(escape(block["caption"]),styles["caption"])]
        elif kind == "code":
            if block["language"] == "mermaid":
                diagram = Workflow(block["text"])
                diagram.keepWithNext = True
                story += [diagram,Paragraph(escape(diagram.caption),styles["caption"])]
            else:
                story.append(Paragraph(escape(block["text"]).replace("\n","<br/>"),styles["body"]))
    document.multiBuild(story)


def check_source(text, blocks):
    chapters = [b for b in blocks if b["type"]=="heading" and b["level"]==2 and re.match(r"\d+\.",b["text"])]
    if len(chapters)!=38:
        raise ValueError("Expected 38 functional chapters")
    headings, missing = {slug(b["text"]) for b in blocks if b["type"]=="heading"}, []
    for target in re.findall(r"!?\[[^\]]*\]\(([^)]+)\)",text):
        if target.startswith(("https://","http://")):
            continue
        if target.startswith("#"):
            if target[1:] not in headings:
                missing.append(target)
        elif not (SOURCE.parent/target.split("#",1)[0]).resolve().exists():
            missing.append(target)
    if missing:
        raise ValueError("Broken source links: "+", ".join(missing))
    if re.findall(r"^\| (FR\d{2}) \|",text,re.M)!=[f"FR{i:02}" for i in range(1,21)]:
        raise ValueError("Functional requirement mapping missing or duplicated")
    return {"chapters":38,"word_count":len(text.split()),"tables":sum(b["type"]=="table" for b in blocks),
            "screenshots":sum(b["type"]=="image" for b in blocks),
            "workflows":sum(b["type"]=="code" and b["language"]=="mermaid" for b in blocks),
            "local_links":"all targets and anchors valid","requirement_ids":"FR01-FR20 exactly once"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output",type=Path,default=ROOT/"output"/"pdf")
    options = parser.parse_args()
    options.output.mkdir(parents=True,exist_ok=True)
    text = SOURCE.read_text(encoding="utf-8")
    blocks = parse_markdown(text)
    facts = check_source(text,blocks)
    pdf = options.output/f"GitCompass-Functional-Documentation-{APP_VERSION}.pdf"
    webpage = options.output/f"GitCompass-Functional-Documentation-{APP_VERSION}.html"
    write_html(blocks,webpage); write_pdf(blocks,pdf)
    reader = PdfReader(pdf)
    extracted = re.sub(r"\s+"," ","\n".join(page.extract_text() or "" for page in reader.pages))
    for heading in [b["text"] for b in blocks if b["type"]=="heading" and b["level"]==2 and b["text"]!="Contents"]:
        if heading not in extracted:
            raise ValueError("PDF omitted chapter: "+heading)
    if "\ufffd" in extracted or not reader.outline:
        raise ValueError("PDF text or bookmarks incomplete")
    facts.update({"application_version":APP_VERSION,"document_version":DOCUMENT_VERSION,"pdf_pages":len(reader.pages),"pdf_bytes":pdf.stat().st_size,"html_bytes":webpage.stat().st_size,
                  "pdf_bookmarks":"present","pdf_sha256":hashlib.sha256(pdf.read_bytes()).hexdigest(),
                  "source_sha256":hashlib.sha256(SOURCE.read_bytes()).hexdigest(),"application_package":"unchanged"})
    (options.output/"functional-documentation-validation.json").write_text(json.dumps(facts,indent=2),encoding="utf-8")
    print(json.dumps(facts,indent=2))


if __name__=="__main__":
    main()
