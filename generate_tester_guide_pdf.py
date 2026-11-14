"""
CSE3705 Artificial Intelligence - Playtester Instruction Guide Generator
Generates a comprehensive, professional 3-page Playtester Instruction Guide PDF
detailing intentional behavioral playstyles, data submission instructions, and the
post-collection ML pipeline.
"""

import os
from reportlab.lib.pagesizes import letter
from reportlab.lib import colors
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak, HRFlowable
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.pdfgen import canvas

PDF_PATH = r"C:\Users\astro\Desktop\College Ayush\GAME\adaptive-boss-arena-main\adaptive-boss-arena-main\Playtester_Instruction_Guide.pdf"
DESKTOP_PDF_PATH = r"C:\Users\astro\Desktop\Playtester_Instruction_Guide.pdf"


class NumberedCanvas(canvas.Canvas):
    def __init__(self, *args, **kwargs):
        super(NumberedCanvas, self).__init__(*args, **kwargs)
        self._saved_page_states = []

    def showPage(self):
        self._saved_page_states.append(dict(self.__dict__))
        self._startPage()

    def save(self):
        num_pages = len(self._saved_page_states)
        for state in self._saved_page_states:
            self.__dict__.update(state)
            self.draw_page_decorations(num_pages)
            canvas.Canvas.showPage(self)
        canvas.Canvas.save(self)

    def draw_page_decorations(self, page_count):
        self.saveState()
        self.setFont('Helvetica', 8)
        self.setFillColor(colors.HexColor('#6b7280'))
        # Header
        self.drawString(40, 762, 'Adaptive Boss Arena — Playtester Guide & Experimental Telemetry Protocol')
        self.setStrokeColor(colors.HexColor('#e5e7eb'))
        self.setLineWidth(0.5)
        self.line(40, 754, 572, 754)
        # Footer
        self.line(40, 36, 572, 36)
        self.drawString(40, 24, 'CSE3705 Artificial Intelligence — Autonomous Adaptive Boss Combat Project')
        page_text = f'Page {self._pageNumber} of {page_count}'
        self.drawRightString(572, 24, page_text)
        self.restoreState()


def build_pdf():
    doc = SimpleDocTemplate(
        PDF_PATH,
        pagesize=letter,
        leftMargin=40,
        rightMargin=40,
        topMargin=46,
        bottomMargin=46
    )

    styles = getSampleStyleSheet()

    primary_color = colors.HexColor('#1e1b4b')  # Dark Indigo
    accent_color = colors.HexColor('#b91c1c')   # Deep Crimson
    accent_gold = colors.HexColor('#b45309')    # Amber / Gold
    accent_green = colors.HexColor('#065f46')   # Dark Emerald
    text_dark = colors.HexColor('#111827')
    bg_light = colors.HexColor('#f9fafb')

    title_style = ParagraphStyle('DocTitle', parent=styles['Normal'], fontName='Helvetica-Bold', fontSize=18, leading=21, textColor=primary_color, spaceAfter=2)
    subtitle_style = ParagraphStyle('DocSubtitle', parent=styles['Normal'], fontName='Helvetica', fontSize=9.5, leading=12, textColor=accent_color, spaceAfter=6)
    h1_style = ParagraphStyle('Heading1', parent=styles['Normal'], fontName='Helvetica-Bold', fontSize=10.5, leading=13, textColor=primary_color, spaceBefore=6, spaceAfter=3)
    body_style = ParagraphStyle('BodyText', parent=styles['Normal'], fontName='Helvetica', fontSize=7.6, leading=9.8, textColor=text_dark, spaceAfter=3)
    bullet_style = ParagraphStyle('BulletText', parent=body_style, leftIndent=10, spaceAfter=2)
    table_header_style = ParagraphStyle('TableHeader', parent=styles['Normal'], fontName='Helvetica-Bold', fontSize=7.2, leading=9, textColor=colors.white, alignment=1)
    table_cell_style = ParagraphStyle('TableCell', parent=styles['Normal'], fontName='Helvetica', fontSize=6.9, leading=8.6, textColor=text_dark)
    table_cell_bold = ParagraphStyle('TableCellBold', parent=styles['Normal'], fontName='Helvetica-Bold', fontSize=6.9, leading=8.6, textColor=text_dark)
    table_cell_center = ParagraphStyle('TableCellCenter', parent=styles['Normal'], fontName='Helvetica', fontSize=6.9, leading=8.6, alignment=1, textColor=text_dark)

    story = []

    # ==================== PAGE 1 ====================
    story.append(Paragraph('Adaptive Boss Arena: Playtester Instruction Guide', title_style))
    story.append(Paragraph('Protocol for Behavioral Telemetry Data Collection & Supervised ML Model Training', ParagraphStyle('SubSub', parent=title_style, fontSize=11, leading=13.5, textColor=colors.HexColor('#374151'))))
    story.append(Paragraph('Academic Research & Game AI Engineering — CSE3705 Artificial Intelligence Project', subtitle_style))
    story.append(HRFlowable(width='100%', thickness=1.2, color=accent_color, spaceAfter=6))

    story.append(Paragraph('1. Welcome & Playtester Mission Briefing', h1_style))
    story.append(Paragraph(
        'Thank you for playtesting <b>Adaptive Boss Arena</b>! '
        'This game features a martial duel inspired by <i>Sekiro: Shadows Die Twice</i> against an adaptive autonomous Boss. '
        'The game contains a built-in telemetry engine that records 52 parameters per frame—including combat physics, spacing, defensive reaction timings, and habit frequencies. '
        'Your gameplay telemetry will directly train our supervised Machine Learning models (Random Forest &amp; Histogram Gradient Boosting).',
        body_style
    ))

    # Controls Table
    controls_data = [
        [Paragraph('Combat Action', table_header_style), Paragraph('Keyboard &amp; Mouse', table_header_style), Paragraph('Gamepad (Xbox / PS)', table_header_style), Paragraph('Combat Function &amp; Tactical Role', table_header_style)],
        [Paragraph('<b>Move / Steer</b>', table_cell_style), Paragraph('W, A, S, D', table_cell_center), Paragraph('Left Stick', table_cell_center), Paragraph('8-directional continuous movement and positioning.', table_cell_style)],
        [Paragraph('<b>Light Attack</b>', table_cell_style), Paragraph('<b>Left Mouse Button</b>', table_cell_center), Paragraph('X / Square', table_cell_center), Paragraph('3-hit rising combo (Slash &rarr; Backslash &rarr; Rising Cut).', table_cell_style)],
        [Paragraph('<b>Heavy Attack</b>', table_cell_style), Paragraph('<b>Shift + Left Mouse</b>', table_cell_center), Paragraph('Right Trigger (RT / R2)', table_cell_center), Paragraph('Crushing overhead smash (42 dmg, 3.5 m/s forward step).', table_cell_style)],
        [Paragraph('<b>Parry (Deflect)</b>', table_cell_style), Paragraph('<b>Right Mouse Button / Q</b>', table_cell_center), Paragraph('Right Bumper (RB / R1)', table_cell_center), Paragraph('<b>0.40s window (~24 frames)</b>: Shatters 45 boss Posture points &amp; hit-stop.', table_cell_bold)],
        [Paragraph('<b>Vertical Jump</b>', table_cell_style), Paragraph('<b>F / Left Alt</b>', table_cell_center), Paragraph('A / Cross', table_cell_center), Paragraph('<b>7.5 m/s launch</b>: Physically clears sweeps, ground slams &amp; fire hazards.', table_cell_style)],
        [Paragraph('<b>Dodge / Dash</b>', table_cell_style), Paragraph('<b>Spacebar</b>', table_cell_center), Paragraph('B / Circle', table_cell_center), Paragraph('0.32s evasive dash with 0.135s invulnerability frames (i-frames).', table_cell_style)],
        [Paragraph('<b>Heal Flask</b>', table_cell_style), Paragraph('E / R', table_cell_center), Paragraph('D-Pad Up', table_cell_center), Paragraph('Restores 45 HP (2 charges per match). Note the long drinking animation lock!', table_cell_style)],
        [Paragraph('<b>Lock-On Camera</b>', table_cell_style), Paragraph('Middle Mouse / Tab', table_cell_center), Paragraph('Right Stick Click (R3)', table_cell_center), Paragraph('Slow vertical camera tracking keeping the boss centered on screen.', table_cell_style)],
    ]
    t_ctrl = Table(controls_data, colWidths=[85, 115, 115, 217])
    t_ctrl.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), primary_color),
        ('GRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#d1d5db')),
        ('ROWBACKGROUNDS', (0, 1), (-1, -1), [colors.white, bg_light]),
        ('TOPPADDING', (0, 0), (-1, -1), 2),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 2),
    ]))
    story.append(t_ctrl)
    story.append(Spacer(1, 5))

    story.append(Paragraph('2. Why We Need Specific Playstyles (The Data Imbalance Problem)', h1_style))
    story.append(Paragraph(
        'In machine learning, models require diverse training samples to generalize well. '
        'In our baseline telemetry dataset (911 frames), player behavior was naturally skewed toward dodging at mid-range: '
        '<b>43.1%</b> of data is <i>Wide Sweep</i> and <b>24.3%</b> is <i>Cataclysmic Wave</i>. '
        'Conversely, critical tactical counter-moves like <b>Jab (2.3%, 16 samples)</b> and <b>Perilous Overhead (1.8%, 13 samples)</b> barely occurred! '
        'If every tester plays casually, the ML classifiers will never observe enough samples to predict these rare moves. '
        'To solve this, we ask each tester to deliberately alternate across <b>5 intentional playstyle archetypes</b>.',
        body_style
    ))

    session_target_data = [
        [Paragraph('<b>Playtesting Target:</b> Play <b>6 to 10 full matches</b>. Alternate between the 5 playstyle archetypes on Page 2 (e.g., 2 matches as The Turtle, 2 matches as The Aggressor, etc.). The game automatically logs every frame into <code>gameplay_telemetry.csv</code> in your game folder!', ParagraphStyle('TargetBox', parent=body_style, textColor=colors.HexColor('#1e3a8a')))]
    ]
    t_box = Table(session_target_data, colWidths=[532])
    t_box.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, -1), colors.HexColor('#eff6ff')),
        ('BOX', (0, 0), (-1, -1), 1, colors.HexColor('#3b82f6')),
        ('TOPPADDING', (0, 0), (-1, -1), 3),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 3),
        ('LEFTPADDING', (0, 0), (-1, -1), 6),
        ('RIGHTPADDING', (0, 0), (-1, -1), 6),
    ]))
    story.append(t_box)

    story.append(PageBreak())

    # ==================== PAGE 2 ====================
    story.append(Paragraph('3. The 5 Playstyle Archetypes & Tactical Combat Protocol', h1_style))
    story.append(Paragraph(
        'Each playstyle triggers a specific adaptive decision heuristic in the Boss AI. '
        'Review the instructions below for how to play, why we do it, and what you must do when the boss counters:',
        body_style
    ))

    archetype_data = [
        [Paragraph('Archetype & Role', table_header_style), Paragraph('How You Must Play', table_header_style), Paragraph('Why We Do It (Boss Trigger)', table_header_style), Paragraph('Target Boss Ability & What You Must Do', table_header_style)],

        [
            Paragraph('<b>1. The Turtle</b><br/><font color="#4b5563">(Defensive Wall)</font>', table_cell_style),
            Paragraph('&bull; Hold <b>Guard</b> continuously.<br/>&bull; Never initiate attacks.<br/>&bull; Absorb hits with block/deflect.<br/>&bull; Play 2 full matches.', table_cell_style),
            Paragraph('Triggers the boss\'s <b>Guard-Crush heuristic</b> (&gt;45% guard reliance). Boss recognizes passive defense and attempts to shatter posture.', table_cell_style),
            Paragraph('<b>Target Move:</b> <font color="#b91c1c"><b>Perilous Overhead (26 dmg)</b></font> &amp; <b>Ground Slam (8 dmg)</b>.<br/><b>What You Must Do:</b> When the boss flashes the <b>Red Kanji</b>, release block immediately and <b>Dodge sideways (Space)</b> or <b>Jump (F)</b>! Blocking will result in an instant guard crush.', table_cell_style)
        ],

        [
            Paragraph('<b>2. The Aggressor</b><br/><font color="#4b5563">(Melee Masher)</font>', table_cell_style),
            Paragraph('&bull; Stay within <b>2.5m</b> at all times.<br/>&bull; Mash <b>Left Click</b> combo.<br/>&bull; Relentless offensive pressure.<br/>&bull; Never back off.<br/>&bull; Play 2 full matches.', table_cell_style),
            Paragraph('Triggers the boss\'s <b>Counter-Aggression heuristic</b> (&gt;55% player attack cadence). Boss deploys fast hyper-armour interrupts.', table_cell_style),
            Paragraph('<b>Target Move:</b> <font color="#b91c1c"><b>Jab (0.28s startup, 8 dmg)</b></font> &amp; Boss Defensive Parry.<br/><b>What You Must Do:</b> Watch for the boss\'s fast wind-up and <b>tap Right-Click Parry</b> instantly! Deflecting the jab deals 28–45 posture damage back to the boss.', table_cell_style)
        ],

        [
            Paragraph('<b>3. The Kiter</b><br/><font color="#4b5563">(Perimeter Runner)</font>', table_cell_style),
            Paragraph('&bull; Maintain distance <b>&gt; 6.0m</b>.<br/>&bull; Run around arena perimeter.<br/>&bull; Refuse close-quarters combat.<br/>&bull; Bait boss at long range.<br/>&bull; Play 2 full matches.', table_cell_style),
            Paragraph('Triggers the boss\'s <b>Anti-Kiting / Gap-Closing heuristic</b> (&gt;5m mean distance). Forces long-distance pursuit and artillery.', table_cell_style),
            Paragraph('<b>Target Move:</b> <font color="#b91c1c"><b>Charge (16 m/s lunge)</b></font> &amp; <b>Shockwave (7m projectile)</b>.<br/><b>What You Must Do:</b> As the boss lunges toward you, time a <b>Right-Click Parry</b> on impact to deflect the charge, or <b>Jump (F)</b> vertically over the expanding shockwave ring!', table_cell_style)
        ],

        [
            Paragraph('<b>4. The Evasive Shinobi</b><br/><font color="#4b5563">(Roll Spammer)</font>', table_cell_style),
            Paragraph('&bull; Constantly spam <b>Spacebar</b> dash.<br/>&bull; Circle around the boss\'s back.<br/>&bull; Rely exclusively on i-frames.<br/>&bull; Never block or parry.<br/>&bull; Play 1–2 matches.', table_cell_style),
            Paragraph('Triggers the boss\'s <b>Roll-Catch heuristic</b> (&gt;40% dodge frequency). The boss deploys wide horizontal arcs.', table_cell_style),
            Paragraph('<b>Target Move:</b> <font color="#b91c1c"><b>Wide Sweep (4.0m, 120° arc)</b></font>.<br/><b>What You Must Do:</b> Rolling into the sweep will clip you! Stop dodging and tap <b>Right-Click Parry</b> or <b>Jump vertically (F)</b> over the horizontal blade to clear it cleanly.', table_cell_style)
        ],

        [
            Paragraph('<b>5. The Deflect Master</b><br/><font color="#4b5563">(Posture Hunter)</font>', table_cell_style),
            Paragraph('&bull; Stand dead center in front.<br/>&bull; Do not roll or run away.<br/>&bull; Purely time <b>Right Click</b> parry.<br/>&bull; Build boss Posture to 100.<br/>&bull; Play 1–2 matches.', table_cell_style),
            Paragraph('Triggers the boss\'s <b>Posture Breakdown State</b>. Shattering 100 Posture points drops the boss onto their knees in a 2.8s stun.', table_cell_style),
            Paragraph('<b>Target Move:</b> <font color="#065f46"><b>Broken Guard Kneeling Stagger (2.8s)</b></font>.<br/><b>What You Must Do:</b> Walk forward within 2.8m and press <b>Left Click</b> to unleash the cinematic <b>Riposte Execution (72 flat damage, unparryable)</b>!', table_cell_style)
        ],
    ]

    t_arch = Table(archetype_data, colWidths=[80, 100, 120, 232])
    t_arch.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), primary_color),
        ('GRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#d1d5db')),
        ('ROWBACKGROUNDS', (0, 1), (-1, -1), [colors.white, bg_light]),
        ('TOPPADDING', (0, 0), (-1, -1), 2.5),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 2.5),
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
    ]))
    story.append(t_arch)
    story.append(Spacer(1, 6))

    story.append(Paragraph('4. What Data We Need From You (Submission Checklist)', h1_style))
    story.append(Paragraph(
        'When you finish your play session, you only need to return <b>one primary file</b> plus a quick 1-minute survey response:',
        body_style
    ))
    story.append(Paragraph('<b>1. The Telemetry CSV File:</b> Inside your unzipped game folder, locate the auto-generated <b><font color="#b91c1c">gameplay_telemetry.csv</font></b>. Rename it with your name (e.g. <code>telemetry_rohit.csv</code> or <code>telemetry_aman.csv</code>) and send it back to the developer.', bullet_style))
    story.append(Paragraph('<b>2. Match Volume:</b> How many total matches did you play? (Target: at least 6 to 10 matches).', bullet_style))
    story.append(Paragraph('<b>3. Qualitative Difficulty Rating:</b> On a scale of 1 to 10, how challenging did the Boss feel across your matches?', bullet_style))
    story.append(Paragraph('<b>4. Most Challenging Attack:</b> Which boss move felt hardest to react to? (Sweep, Ground Slam, Charge, Shockwave, Jab, or Perilous Overhead?)', bullet_style))
    story.append(Paragraph('<b>5. Hardware &amp; Performance:</b> Did you play on Keyboard+Mouse or Gamepad? Did the game run at a smooth 60 FPS without frame drops?', bullet_style))

    story.append(PageBreak())

    # ==================== PAGE 3 ====================
    story.append(Paragraph('5. After Collecting the Data: What We Will Do (The ML Pipeline)', h1_style))
    story.append(Paragraph(
        'Once telemetry files from all playtesters are submitted, we execute an end-to-end Machine Learning data engineering and model training pipeline:',
        body_style
    ))

    pipeline_data = [
        [Paragraph('Pipeline Stage', table_header_style), Paragraph('Technical Action', table_header_style), Paragraph('Tools &amp; Algorithms', table_header_style), Paragraph('Engine &amp; Research Outcome', table_header_style)],
        [Paragraph('<b>1. Multi-Player Aggregation</b>', table_cell_bold), Paragraph('Combine all testers\' CSV files into a unified master dataset with sequential global game IDs and player source tags.', table_cell_style), Paragraph('Python, Pandas (<font color="#065f46">merge_telemetry.py</font>)', table_cell_style), Paragraph('Produces <font color="#1e3a8a">master_telemetry.csv</font> &amp; Excel with thousands of synchronized decision frames.', table_cell_style)],
        [Paragraph('<b>2. Action Space Grouping</b>', table_cell_bold), Paragraph('Cluster the 7 raw attacks into 4 balanced tactical archetypes: <i>MeleeStrike</i>, <i>RangedThreat</i>, <i>HeavyCrush</i>, and <i>GapCloser</i>.', table_cell_style), Paragraph('Multiclass Label Encoding, Stratified Splitting', table_cell_style), Paragraph('Eliminates rare class starvation (Jab/Overhead) and elevates Macro-F1 score past <b>0.80</b>.', table_cell_style)],
        [Paragraph('<b>3. Feature Pruning</b>', table_cell_bold), Paragraph('Prune 100% collinear duplicate columns down to 8 core orthogonal signals (Distance, Angle, Guard, Posture %, etc.).', table_cell_style), Paragraph('Feature Importance, Correlation Matrix', table_cell_style), Paragraph('Removes curse of dimensionality, prevents tree overfitting, and cuts inference latency below 2ms.', table_cell_style)],
        [Paragraph('<b>4. Supervised Model Training</b>', table_cell_bold), Paragraph('Train 4 competitive classifier architectures and select top performer on hold-out test set.', table_cell_style), Paragraph('Logistic Reg, Decision Tree, Random Forest, <b>HistGradientBoosting</b>', table_cell_style), Paragraph('Generates production artifact <font color="#b45309">boss_action_model.pkl</font> achieving &gt;75% prediction accuracy.', table_cell_style)],
        [Paragraph('<b>5. Live Model Deployment</b>', table_cell_bold), Paragraph('Embed the trained classifier weights directly into Unity to drive real-time Boss tactical decision making.', table_cell_style), Paragraph('C# ONNX Runtime / Serialized Tree Inference', table_cell_style), Paragraph('Replaces hardcoded static rules with a live neural/gradient-boosted adaptive brain.', table_cell_style)],
    ]
    t_pipe = Table(pipeline_data, colWidths=[85, 140, 115, 192])
    t_pipe.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), primary_color),
        ('GRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#d1d5db')),
        ('ROWBACKGROUNDS', (0, 1), (-1, -1), [colors.white, bg_light]),
        ('TOPPADDING', (0, 0), (-1, -1), 2),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 2),
    ]))
    story.append(t_pipe)
    story.append(Spacer(1, 6))

    # Big Question: Will the Boss Become Tougher?
    story.append(Paragraph('6. Will the Boss Fight Become Tougher? (Deep Analysis)', h1_style))
    story.append(Paragraph(
        '<b>YES — but it becomes INTELLIGENTLY TOUGHER, not "cheaply" or "artificially" tougher.</b>',
        ParagraphStyle('HighlightCallout', parent=body_style, fontName='Helvetica-Bold', fontSize=8, leading=10, textColor=accent_color)
    ))

    story.append(Paragraph(
        'In conventional game development, making a boss harder usually means inflating raw statistics: giving the boss 10,000 HP, doubling damage, or reducing reaction windows to 0.05 seconds. '
        'That is artificial difficulty, which creates player frustration rather than mastery. '
        'In our Machine Learning implementation, the Boss becomes tougher through <b>Cognitive Pattern Recognition and Habit Conditioning</b>:',
        body_style
    ))

    cards_data = [
        [
            Paragraph('<b>1. Roll-Spam Exploitation &amp; Directional Prediction</b>', table_cell_bold),
            Paragraph('The ML model tracks your dodge frequency and directional bias. If you repeatedly roll to the left upon seeing an attack startup, the model predicts your roll trajectory within 2 frames and pre-emptively queues <b>Wide Sweep</b> with a 120° arc to catch your recovery frames as you exit i-frames.', table_cell_style)
        ],
        [
            Paragraph('<b>2. Anti-Turtling &amp; Posture Shatter Conditioning</b>', table_cell_bold),
            Paragraph('If you play passively and hold guard, the ML model recognizes your guard reliance metric rising above 0.50. Instead of throwing standard swings that you can easily block, it immediately transitions to <b>Perilous Overhead (Unblockable Red Kanji)</b>, forcing you out of your comfort zone.', table_cell_style)
        ],
        [
            Paragraph('<b>3. Estus / Flask Punish (Healing Window Exploitation)</b>', table_cell_bold),
            Paragraph('When a player is hurt, human psychology causes them to instinctively back away to drink a heal flask. The ML model correlates player distance (&gt;6m) + low player HP, immediately queuing <b>Charge (16 m/s gap closer)</b> or <b>Shockwave</b> straight into your 1.2-second healing animation lock.', table_cell_style)
        ],
        [
            Paragraph('<b>4. Combo Extension &amp; Dynamic Pacing</b>', table_cell_bold),
            Paragraph('Against hesitant players, the ML Boss dials down recovery cooldowns between attacks (from 0.85s down to 0.35s), applying suffocating Sekiro-style martial pressure. If the player demonstrates flawless deflecting, the boss feints attack startups to bait early parry attempts and punish the cooldown window.', table_cell_style)
        ],
    ]
    t_cards = Table(cards_data, colWidths=[150, 382])
    t_cards.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, -1), bg_light),
        ('GRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#d1d5db')),
        ('LINEBEFORE', (0, 0), (0, -1), 3, accent_color),
        ('TOPPADDING', (0, 0), (-1, -1), 2.5),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 2.5),
    ]))
    story.append(t_cards)
    story.append(Spacer(1, 5))

    story.append(Paragraph(
        '<b>The Final Experience:</b> Fighting the ML-trained Boss will feel like sparring against a real human martial artist who is actively reading your muscle-memory habits, learning your weaknesses, and adapting in real-time to defeat your specific playstyle!',
        ParagraphStyle('FinalNote', parent=body_style, fontName='Helvetica-Oblique', fontSize=7.6, leading=9.8, textColor=primary_color)
    ))

    doc.build(story, canvasmaker=NumberedCanvas)
    print('Playtester Instruction Guide PDF successfully built at:', PDF_PATH)

    # Copy to Desktop
    import shutil
    shutil.copyfile(PDF_PATH, DESKTOP_PDF_PATH)
    print('Copied to Desktop at:', DESKTOP_PDF_PATH)


if __name__ == '__main__':
    build_pdf()

