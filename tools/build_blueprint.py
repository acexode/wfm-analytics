"""Render the accepted Markdown baseline into its consolidated PDF reading copy.

Run with the Codex bundled Python runtime. No network access is used.
"""
from pathlib import Path
from html import escape
import re

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, PageBreak,
    KeepTogether, Table, TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/pdf/BPO_Workforce_Analytics_Project_Blueprint.pdf'
FONT_DIR = Path.home() / '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/libreoffice-headless/libreoffice/LibreOfficeDev.app/Contents/Resources/fonts/truetype'
for name, file in [('Body', 'LiberationSans-Regular.ttf'), ('Bold', 'LiberationSans-Bold.ttf'), ('Italic', 'LiberationSans-Italic.ttf')]:
    pdfmetrics.registerFont(TTFont(name, str(FONT_DIR / file)))
pdfmetrics.registerFontFamily('Body', normal='Body', bold='Bold', italic='Italic', boldItalic='Bold')

INK = colors.HexColor('#172334')
MUTED = colors.HexColor('#576372')
BLUE = colors.HexColor('#234B68')
STYLES = {
    'body': ParagraphStyle('body', fontName='Body', fontSize=10.4, leading=14.3, textColor=INK, spaceAfter=7, allowWidows=0, allowOrphans=0),
    'bullet': ParagraphStyle('bullet', fontName='Body', fontSize=10.4, leading=14.3, textColor=INK, leftIndent=12, firstLineIndent=-9, spaceAfter=6, allowWidows=0, allowOrphans=0),
    'title': ParagraphStyle('title', fontName='Bold', fontSize=27, leading=31, textColor=colors.black, spaceAfter=16, keepWithNext=True),
    'part': ParagraphStyle('part', fontName='Bold', fontSize=23, leading=27, textColor=colors.black, spaceAfter=13, keepWithNext=True),
    'h2': ParagraphStyle('h2', fontName='Bold', fontSize=14, leading=18, textColor=colors.black, spaceBefore=13, spaceAfter=7, keepWithNext=True),
    'h3': ParagraphStyle('h3', fontName='Bold', fontSize=11.3, leading=15, textColor=colors.black, spaceBefore=9, spaceAfter=5, keepWithNext=True),
    'meta': ParagraphStyle('meta', fontName='Body', fontSize=9, leading=12, textColor=MUTED, spaceAfter=14),
    'label': ParagraphStyle('label', fontName='Bold', fontSize=9, leading=12, textColor=BLUE, spaceAfter=12),
    'reference': ParagraphStyle('reference', fontName='Body', fontSize=9, leading=11.5, textColor=INK, spaceAfter=5, allowWidows=0, allowOrphans=0),
    'table': ParagraphStyle('table', fontName='Body', fontSize=10, leading=14, textColor=INK),
    'tablehead': ParagraphStyle('tablehead', fontName='Bold', fontSize=10, leading=14, textColor=colors.white),
}

def inline(text):
    text = escape(text)
    text = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', text)
    text = re.sub(r'(https://[^\s]+)', lambda m: '<link href="' + m.group(1) + '" color="#234B68">' + m.group(1) + '</link>', text)
    return text

class Blueprint(BaseDocTemplate):
    def __init__(self, path):
        super().__init__(str(path), pagesize=A4, leftMargin=50, rightMargin=50, topMargin=49, bottomMargin=45,
                         title='BPO Workforce Operations Analytics Project Blueprint', author='Project Planning Team')
        self.addPageTemplates(PageTemplate(id='main', frames=[Frame(50, 45, A4[0]-100, A4[1]-94, id='normal', leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)], onPage=self.decorate))

    def decorate(self, canvas, doc):
        canvas.saveState()
        canvas.setFont('Body', 8)
        canvas.setFillColor(MUTED)
        if doc.page > 1:
            canvas.drawString(50, A4[1]-28, 'BPO WORKFORCE OPERATIONS ANALYTICS')
        canvas.drawString(50, 24, 'Internal project plan | Version 1.0 | 12 September 2026')
        canvas.drawRightString(A4[0]-50, 24, str(doc.page))
        canvas.restoreState()

    def afterFlowable(self, flow):
        if isinstance(flow, Paragraph) and hasattr(flow, 'toc_level'):
            self.canv.bookmarkPage(flow.bookmark)
            self.canv.addOutlineEntry(flow.getPlainText(), flow.bookmark, flow.toc_level, False)
            self.notify('TOCEntry', (flow.toc_level, flow.getPlainText(), self.page, flow.bookmark))

def para(text, style='body'):
    return Paragraph(inline(text), STYLES[style])

story = [Spacer(1, 14), para('PROJECT BLUEPRINT', 'label'),
         para('BPO Workforce<br/>Operations Analytics'.replace('<br/>', '\n'), 'title')]
# Keep title line breaks intentional without treating source text as HTML.
story[-1] = Paragraph('BPO Workforce<br/>Operations Analytics', STYLES['title'])
story += [para('Unified product definition, technical implementation plan, and AI delivery model', 'h3'),
          para('Version 1.0 | 12 September 2026', 'meta'), Spacer(1, 8),
          para('Build an internal system that helps managers explain scheduled capacity, connect it to authoritative work output, and resolve operational exceptions using trustworthy evidence.'),
          para('The first release uses a Windows collector, a management web interface, and PostgreSQL on suitable company infrastructure. The plan prioritizes accurate definitions, minimal collection, bounded operating cost, and a measured pilot.'),
          para('Decisions at a glance', 'h2')]
rows = [['Decision', 'First release baseline'],
        ['Deployment', 'One company, existing infrastructure where suitable'],
        ['Core stack', 'C# Windows collector, ASP.NET Core, React, PostgreSQL'],
        ['Additional services', 'No ClickHouse or paid runtime AI dependency'],
        ['Pilot ladder', '25 to 50 employees, then 200 after evidence gates'],
        ['Measurement', 'Schedules, activity evidence, approved context, and imported output'],
        ['Delivery control', 'AI lead, bounded specialist work, independent review']]
table=Table([[para(c, 'tablehead' if i==0 else 'table') for c in row] for i,row in enumerate(rows)], colWidths=[125, A4[0]-225], repeatRows=1, hAlign='LEFT')
table.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),BLUE),('GRID',(0,0),(-1,-1),.45,colors.HexColor('#D9D9D9')),('VALIGN',(0,0),(-1,-1),'MIDDLE'),('LEFTPADDING',(0,0),(-1,-1),9),('RIGHTPADDING',(0,0),(-1,-1),9),('TOPPADDING',(0,0),(-1,-1),8),('BOTTOMPADDING',(0,0),(-1,-1),8),('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white,colors.HexColor('#F3F6F8')])]))
story += [table, Spacer(1, 12), para('Planning status', 'h2'), para('The three original sketches have been consolidated and reviewed. Company-specific assumptions and acceptance targets are explicit. This document is an implementation baseline; it does not claim that software has been built, tested, or deployed.')]
story += [PageBreak(), para('Contents', 'part')]
toc = TableOfContents(tableStyle=TableStyle([('TOPPADDING',(0,0),(-1,-1),0),('BOTTOMPADDING',(0,0),(-1,-1),0),('LEFTPADDING',(0,0),(-1,-1),0),('RIGHTPADDING',(0,0),(-1,-1),0)]))
toc.levelStyles = [ParagraphStyle('toc0', fontName='Bold', fontSize=11, leading=15, spaceBefore=10, spaceAfter=3), ParagraphStyle('toc1',fontName='Body',fontSize=9.4,leading=13,leftIndent=13,firstLineIndent=0,spaceBefore=1)]
story += [toc]
files = ['01-project-definition.md', '02-technical-implementation-plan.md', '03-agent-delivery-model.md']
for part_no, filename in enumerate(files,1):
    story.append(PageBreak())
    for line_no, line in enumerate((ROOT/'docs'/filename).read_text().splitlines()):
        if not line.strip():
            continue
        if line.startswith('# '):
            story.append(para(f'PART {part_no}', 'label'))
            titles = ['Project Definition', 'Technical Implementation Plan', 'AI Team Delivery and Project Control']
            p=para(titles[part_no-1], 'part');p.toc_level=0;p.bookmark=f'part{part_no}';story.append(p)
        elif line.startswith('## '):
            p=para(line[3:], 'h2');p.toc_level=1;p.bookmark=f'p{part_no}s{line_no}';story.append(p)
        elif line.startswith('### '):
            story.append(para(line[4:], 'h3'))
        elif line.startswith('- '):
            story.append(para('• '+line[2:], 'bullet'))
        elif line.startswith('Version '):
            story.append(para(line, 'meta'))
        elif line.startswith('[S'):
            story.append(para(line, 'reference'))
        else:
            story.append(para(line))

OUT.parent.mkdir(parents=True, exist_ok=True)
Blueprint(OUT).multiBuild(story)
print(OUT)
