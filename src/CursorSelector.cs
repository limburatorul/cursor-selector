// Cursor Selector - client de management pentru cursoarele Windows.
// Se compileaza cu csc.exe din .NET Framework (vezi build.cmd), fara dependinte externe.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CursorSelector
{
    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadCursorFromFile(string lpFileName);

        // Spre deosebire de LoadCursorFromFile (care ia marimea sistemului, de obicei 32px),
        // LoadImage alege din .cur cadrul cel mai apropiat de marimea ceruta.
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadImage(IntPtr hinst, string name, uint type, int cx, int cy, uint load);
        internal const uint IMAGE_CURSOR = 2, LR_LOADFROMFILE = 0x10;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
            int cx, int cy, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyCursor(IntPtr hCursor);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        internal static extern bool InvalidateRect(IntPtr hwnd, ref RECT rect, bool erase);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        // Nedocumentat, dar singura cale de a obtine bare de derulare intunecate pe controalele native.
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int mode);

        internal static void TryEnableDarkControls()
        {
            try { SetPreferredAppMode(2); }   // 2 = ForceDark
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        internal const uint SPI_SETCURSORS = 0x0057;
        internal const uint SPIF_UPDATEINIFILE_SENDCHANGE = 0x03;
        internal const int DI_NORMAL = 0x0003;
    }

    /// <summary>
    /// Paleta si tipografia din nota de branding. Acolo panourile si muchiile sunt alb cu alfa,
    /// ca sa stea la fel peste orice fundal; aici sunt compuse in culori opace, fiindca WinForms
    /// deseneaza controale opace si nu are un strat de compunere sub ele.
    /// </summary>
    internal static class Theme
    {
        internal static readonly Color Ground = Color.FromArgb(0x0A, 0x0D, 0x13);
        internal static readonly Color Ground2 = Color.FromArgb(0x0D, 0x11, 0x1A);

        internal static readonly Color Text = Color.FromArgb(0xE8, 0xEE, 0xF6);
        internal static readonly Color Dim = Color.FromArgb(0x8A, 0x97, 0xAA);
        internal static readonly Color Dimmer = Color.FromArgb(0x5D, 0x68, 0x79);

        internal static readonly Color Accent = Color.FromArgb(0x4C, 0x8D, 0xFF);
        internal static readonly Color AccentHover = Color.FromArgb(0x6B, 0xA2, 0xFF);
        internal static readonly Color AccentInk = Color.FromArgb(0x06, 0x10, 0x1F);

        private static Color Mix(Color under, Color over, double alpha)
        {
            return Color.FromArgb(
                (int)Math.Round(under.R + (over.R - under.R) * alpha),
                (int)Math.Round(under.G + (over.G - under.G) * alpha),
                (int)Math.Round(under.B + (over.B - under.B) * alpha));
        }

        private static Color Over(Color ground, double alpha) { return Mix(ground, Color.White, alpha); }

        internal static readonly Color Panel = Over(Ground, 0.038);
        internal static readonly Color Panel2 = Over(Ground, 0.06);
        internal static readonly Color Edge = Over(Ground, 0.085);
        internal static readonly Color EdgeBright = Over(Ground, 0.18);
        internal static readonly Color PanelOnSide = Over(Ground2, 0.038);
        internal static readonly Color AccentMuted = Mix(Ground, Accent, 0.28);
        internal static readonly Color SelectedRow = Mix(Ground2, Accent, 0.17);

        // Gri mediu: singurul fundal pe care se vad si cursoarele negre, si cele albe.
        internal static readonly Color Swatch = Color.FromArgb(0xB4, 0xB8, 0xC0);
        internal static readonly Color SwatchEmpty = Over(Ground, 0.05);

        internal const int Radius = 14;        // --r
        internal const int RadiusLarge = 20;   // --r-lg

        // Stack-urile de fonturi din nota. Prima familie instalata castiga, deci daca ajung
        // vreodata pe masina fonturile proprii, aplicatia le preia fara nicio modificare.
        private static readonly string[] DisplayStack = { "Bricolage Grotesque", "Segoe UI" };
        private static readonly string[] BodyStack = { "IBM Plex Sans", "Segoe UI" };
        private static readonly string[] MonoStack = { "IBM Plex Mono", "Cascadia Mono", "Consolas" };

        private static readonly HashSet<string> Installed = LoadInstalledFamilies();

        private static HashSet<string> LoadInstalledFamilies()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var collection = new System.Drawing.Text.InstalledFontCollection())
                foreach (var family in collection.Families) names.Add(family.Name);
            return names;
        }

        private static Font Pick(string[] stack, float size, FontStyle style)
        {
            foreach (var name in stack)
                if (Installed.Contains(name)) return new Font(name, size, style);
            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        internal static readonly Font FontDisplay = Pick(DisplayStack, 18F, FontStyle.Bold);
        internal static readonly Font FontBody = Pick(BodyStack, 10F, FontStyle.Regular);
        internal static readonly Font FontSmall = Pick(BodyStack, 9F, FontStyle.Regular);
        internal static readonly Font FontButton = Pick(BodyStack, 10F, FontStyle.Regular);
        internal static readonly Font FontButtonBold = Pick(BodyStack, 10.5F, FontStyle.Bold);
        internal static readonly Font FontMono = Pick(MonoStack, 9F, FontStyle.Regular);
        internal static readonly Font FontEyebrow = Pick(MonoStack, 8.5F, FontStyle.Regular);
        internal static readonly Font FontBadge = Pick(MonoStack, 7.5F, FontStyle.Regular);

        internal static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height) / 2) * 2);
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void FillRounded(Graphics g, Rectangle r, int radius, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = RoundedPath(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        internal static void DrawRounded(Graphics g, Rectangle r, int radius, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = RoundedPath(r, radius))
            using (var pen = new Pen(color))
                g.DrawPath(pen, path);
        }

        /// <summary>
        /// Umbra colorata a butonului principal, aproximata prin inele concentrice: GDI+ nu are blur,
        /// iar o umbra desenata astfel ramane in limitele controlului, deci nu cere ajutorul parintelui.
        /// </summary>
        internal static void DrawGlow(Graphics g, Rectangle pill, int radius, Color color, int spread)
        {
            for (int i = spread; i >= 1; i--)
            {
                var ring = Rectangle.Inflate(pill, i, i);
                ring.Offset(0, (i + 1) / 2);
                using (var pen = new Pen(Color.FromArgb(Math.Max(5, 40 / i), color), 2f))
                using (var path = RoundedPath(ring, radius + i))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// GDI+ nu are letter-spacing, iar etichetele eyebrow din identitate depind de el.
        /// Desenam caracter cu caracter, cu avansul acumulat in virgula mobila ca sa nu derive.
        /// </summary>
        internal static void DrawTracked(Graphics g, string text, Font font, Point at, Color color, float tracking)
        {
            float x = at.X;
            foreach (char c in text)
            {
                string one = c.ToString();
                TextRenderer.DrawText(g, one, font, new Point((int)Math.Round(x), at.Y), color,
                    TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(g, one, font, Size.Empty, TextFormatFlags.NoPadding).Width + tracking;
            }
        }

        internal static int MeasureTracked(Graphics g, string text, Font font, float tracking)
        {
            float x = 0;
            foreach (char c in text)
                x += TextRenderer.MeasureText(g, c.ToString(), font, Size.Empty, TextFormatFlags.NoPadding).Width + tracking;
            return (int)Math.Round(Math.Max(0, x - tracking));
        }

        internal static int LineHeight(Graphics g, Font font)
        {
            return TextRenderer.MeasureText(g, "Hg", font, Size.Empty, TextFormatFlags.NoPadding).Height;
        }
    }

    /// <summary>Deseneaza un cursor (.cur sau .ani) pe o pastila gri si il anima cadru cu cadru.</summary>
    internal sealed class PreviewBox : Control
    {
        public IntPtr CursorHandle = IntPtr.Zero;
        public int Step;
        public bool Animated = true;
        public int DrawSize = 64;

        public PreviewBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        public void Reset(IntPtr handle)
        {
            CursorHandle = handle;
            Step = 0;
            Animated = true;
            Invalidate();
        }

        public void Advance()
        {
            if (Animated && CursorHandle != IntPtr.Zero)
            {
                Step++;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var swatch = new Rectangle(0, 0, Width - 1, Height - 1);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRounded(g, swatch, Theme.Radius, CursorHandle == IntPtr.Zero ? Theme.SwatchEmpty : Theme.Swatch);
            g.SmoothingMode = SmoothingMode.Default;

            if (CursorHandle == IntPtr.Zero)
            {
                TextRenderer.DrawText(g, "\u2014", Theme.FontBody, ClientRectangle, Theme.Dimmer,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int x = (Width - DrawSize) / 2;
            int y = (Height - DrawSize) / 2;
            IntPtr hdc = g.GetHdc();
            bool ok = Native.DrawIconEx(hdc, x, y, CursorHandle, DrawSize, DrawSize, Step, IntPtr.Zero, Native.DI_NORMAL);
            g.ReleaseHdc(hdc);

            // Cursor static: pasul 1 esueaza, revenim la cadrul 0 si oprim animatia.
            if (!ok && Step != 0)
            {
                Step = 0;
                Animated = false;
                Invalidate();
            }
        }
    }

    internal sealed class FlatButton : Control
    {
        internal bool Primary;
        private bool _hover;
        private bool _pressed;

        internal FlatButton(string text, bool primary)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Text = text;
            Primary = primary;
            Font = primary ? Theme.FontButtonBold : Theme.FontButton;
            BackColor = Theme.Ground2;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }

        // Un control desenat manual nu mosteneste activarea de la tastatura a unui Button nativ,
        // asa ca o implementam explicit: Tab pentru focus, Space sau Enter pentru apasare.
        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { _pressed = true; Invalidate(); }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (_pressed && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                _pressed = false;
                Invalidate();
                OnClick(EventArgs.Empty);
            }
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Butonul principal isi deseneaza haloul in interiorul propriilor limite, deci
            // pilula lui e retrasa; cel fantoma nu are halou si ocupa tot controlul.
            int inset = Primary ? 7 : 0;
            var pill = Rectangle.Inflate(new Rectangle(0, 0, Width - 1, Height - 1), -inset, -inset);
            int radius = pill.Height / 2;

            Color textColor;
            if (Primary)
            {
                Color fill = !Enabled ? Theme.AccentMuted : _hover && !_pressed ? Theme.AccentHover : Theme.Accent;
                textColor = Enabled ? Theme.AccentInk : Theme.Dimmer;
                if (Enabled) Theme.DrawGlow(g, pill, radius, Theme.Accent, inset);
                Theme.FillRounded(g, pill, radius, fill);
            }
            else
            {
                Color fill = _pressed ? Theme.Edge : _hover ? Theme.Panel2 : Theme.Panel;
                textColor = Enabled ? Theme.Text : Theme.Dimmer;
                Theme.FillRounded(g, pill, radius, fill);
                Theme.DrawRounded(g, pill, radius, Enabled ? Theme.EdgeBright : Theme.Edge);
            }

            TextRenderer.DrawText(g, Text, Font, pill, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused && Enabled)
                Theme.DrawRounded(g, Rectangle.Inflate(pill, -3, -3), radius, Primary ? Color.White : Theme.Accent);
        }
    }

    /// <summary>
    /// Antetul de sectiune din identitate: punct de accent cu halou, eticheta eyebrow si o linie
    /// care se stinge spre dreapta.
    /// </summary>
    internal sealed class Rail : Control
    {
        internal Rail(string label)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
            Text = label.ToUpperInvariant();
            Height = 24;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int mid = Height / 2;

            for (int r = 8; r >= 3; r--)
                using (var brush = new SolidBrush(Color.FromArgb(r == 3 ? 255 : 30 - (r - 3) * 5, Theme.Accent)))
                    g.FillEllipse(brush, 8 - r, mid - r, r * 2, r * 2);

            const float tracking = 1.3f;
            int left = 20;
            int baseline = mid - Theme.LineHeight(g, Theme.FontEyebrow) / 2;
            Theme.DrawTracked(g, Text, Theme.FontEyebrow, new Point(left, baseline), Theme.Dim, tracking);

            int lineLeft = left + Theme.MeasureTracked(g, Text, Theme.FontEyebrow, tracking) + 12;
            if (lineLeft >= Width - 8) return;
            var span = new Rectangle(lineLeft, mid - 1, Width - lineLeft, 3);
            using (var brush = new LinearGradientBrush(span, Theme.EdgeBright, Color.FromArgb(0, Theme.Edge),
                                                       LinearGradientMode.Horizontal))
                g.FillRectangle(brush, lineLeft, mid, span.Width, 1);
        }
    }

    /// <summary>Eticheta cu letter-spacing, pe care un Label obisnuit nu-l poate face.</summary>
    internal sealed class TrackedLabel : Control
    {
        internal float Tracking;
        internal Color Ink = Theme.Text;

        internal TrackedLabel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            int top = (Height - Theme.LineHeight(e.Graphics, Font)) / 2;
            Theme.DrawTracked(e.Graphics, Text, Font, new Point(0, top), Ink, Tracking);
        }
    }

    /// <summary>Zona de continut: card cu colturi rotunjite, cu derulare.</summary>
    internal sealed class CardPanel : FlowLayoutPanel
    {
        internal CardPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Ground;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Ground);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(e.Graphics, r, Theme.RadiusLarge, Theme.Panel);
            Theme.DrawRounded(e.Graphics, r, Theme.RadiusLarge, Theme.Edge);
        }
    }

    /// <summary>
    /// ListBox fara stergerea fundalului sub randuri: fiecare rand isi umple singur dreptunghiul,
    /// deci stergerea de dinainte era doar un cadru gol vizibil (flicker). Umplem doar zona de sub
    /// ultimul rand, pe care nu o picteaza nimeni altcineva.
    /// </summary>
    internal sealed class SchemeList : ListBox
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014)   // WM_ERASEBKGND
            {
                int bottom = Items.Count == 0 ? 0 : Math.Max(0, GetItemRectangle(Items.Count - 1).Bottom);
                if (bottom < ClientSize.Height)
                    using (var g = Graphics.FromHdc(m.WParam))
                    using (var brush = new SolidBrush(BackColor))
                        g.FillRectangle(brush, 0, bottom, ClientSize.Width, ClientSize.Height - bottom);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// WS_EX_COMPOSITED: Windows compune tot arborele de copii intr-un buffer inainte de afisare,
    /// deci placile, etichetele si titlul se schimba dintr-o data la selectarea altei scheme.
    /// </summary>
    internal sealed class ComposedPanel : Panel
    {
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }
        }
    }

    internal sealed class Scheme
    {
        public string Name;
        public bool FromLibrary;
        public string Location;
        public readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);

        // Eticheta e doar pentru afisare. Logica se bazeaza pe FromLibrary, ca traducerea
        // interfetei sa nu mai poata rupe tacut comparatiile.
        public string SourceLabel { get { return FromLibrary ? "Library" : "System"; } }
    }

    internal static class Roles
    {
        // Ordinea canonica Windows: aceeasi in registru si in sirul unei scheme.
        internal static readonly string[] Order =
        {
            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No",
            "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person"
        };

        internal static readonly Dictionary<string, string> Display = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Arrow",       "Normal Select" },
            { "Help",        "Help Select" },
            { "AppStarting", "Working In Background" },
            { "Wait",        "Busy" },
            { "Crosshair",   "Precision Select" },
            { "IBeam",       "Text Select" },
            { "NWPen",       "Handwriting" },
            { "No",          "Unavailable" },
            { "SizeNS",      "Vertical Resize" },
            { "SizeWE",      "Horizontal Resize" },
            { "SizeNWSE",    "Diagonal Resize 1" },
            { "SizeNESW",    "Diagonal Resize 2" },
            { "SizeAll",     "Move" },
            { "UpArrow",     "Alternate Select" },
            { "Hand",        "Link Select" },
            { "Pin",         "Location Select" },
            { "Person",      "Person Select" }
        };
    }

    internal static class Library
    {
        // Doua treceri: intai tipare specifice, apoi cuvinte-cheie scurte peste ce a ramas.
        private static readonly Dictionary<string, string[]> Tier1 = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "Arrow",       new[] { "normalselect", "^normal$", "^arrow$", "^pointer$", "^default$", "^standard$" } },
            { "Help",        new[] { "helpselect", "^help$" } },
            { "AppStarting", new[] { "appstarting", "workinginbackground", "^working$", "background", "working", "work" } },
            { "Wait",        new[] { "^wait$", "busy", "hourglass", "loading" } },
            { "Crosshair",   new[] { "precisionselect", "precision", "^crosshair$", "^cross$" } },
            { "IBeam",       new[] { "textselect", "^text$", "^ibeam$", "^beam$", "ibeam" } },
            { "NWPen",       new[] { "handwriting", "handwrite", "nwpen", "^pen$" } },
            { "No",          new[] { "^no$", "unavailable", "unavail", "nodrop", "forbidden" } },
            { "SizeNS",      new[] { "verticalresize", "resizevertical", "sizens", "vertical", "^vert$", "^ns$" } },
            { "SizeWE",      new[] { "horizontalresize", "resizehorizontal", "sizewe", "horizontal", "^horz$", "^we$" } },
            { "SizeNWSE",    new[] { "diagonalresize1", "sizenwse", "diagonal1", "diag1", "dgn1", "dng1", "resize1", "^nwse$" } },
            { "SizeNESW",    new[] { "diagonalresize2", "sizenesw", "diagonal2", "diag2", "dgn2", "dng2", "resize2", "^nesw$" } },
            { "SizeAll",     new[] { "sizeall", "^move$", "move" } },
            { "UpArrow",     new[] { "alternateselect", "alternate", "uparrow", "^up$" } },
            { "Hand",        new[] { "linkselect", "link", "^hand$" } },
            { "Pin",         new[] { "locationselect", "location", "^pin$", "pin" } },
            { "Person",      new[] { "personselect", "person" } }
        };

        private static readonly Dictionary<string, string[]> Tier2 = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "Arrow",     new[] { "pointer", "arrow", "normal" } },
            { "Help",      new[] { "help" } },
            { "IBeam",     new[] { "beam", "text" } },
            { "NWPen",     new[] { "pen", "hand" } },
            { "Wait",      new[] { "wait" } },
            { "Crosshair", new[] { "cross" } },
            { "SizeNS",    new[] { "vert" } },
            { "SizeWE",    new[] { "horz", "hori" } },
            { "SizeNWSE",  new[] { "nwse", "res1", "dia1" } },
            { "SizeNESW",  new[] { "nesw", "res2", "dia2" } },
            { "UpArrow",   new[] { "alt" } }
        };

        /// <summary>"Diagonal Resize 1.cur" -> "diagonalresize1"</summary>
        private static string Flat(string path)
        {
            return Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9]", "").ToLowerInvariant();
        }

        private static bool IsCursorFile(FileInfo f)
        {
            return f.Extension.Equals(".cur", StringComparison.OrdinalIgnoreCase)
                || f.Extension.Equals(".ani", StringComparison.OrdinalIgnoreCase);
        }

        private static void ResolveByFileName(FileInfo[] files, Dictionary<string, string> map)
        {
            ResolveByFileName(files, map, Tier1, Tier2);
        }

        private static void ResolveByFileName(FileInfo[] files, Dictionary<string, string> map,
                                              params Dictionary<string, string[]>[] tiers)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tier in tiers)
            {
                foreach (var role in Roles.Order)
                {
                    if (map.ContainsKey(role)) continue;
                    string[] patterns;
                    if (!tier.TryGetValue(role, out patterns)) continue;

                    // Wait si AppStarting sunt animate in mod normal, restul nu.
                    bool wantAni = role == "Wait" || role == "AppStarting";
                    foreach (var pattern in patterns)
                    {
                        string p = pattern;
                        var hit = files
                            .Where(f => !used.Contains(f.FullName) && Regex.IsMatch(Flat(f.FullName), p))
                            .OrderBy(f => f.Extension.Equals(".ani", StringComparison.OrdinalIgnoreCase) == wantAni ? 0 : 1)
                            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
                        if (hit == null) continue;
                        map[role] = hit.FullName;
                        used.Add(hit.FullName);
                        break;
                    }
                }
            }
        }

        private static bool ResolveByJson(string dir, Dictionary<string, string> map, ref string name)
        {
            string jsonPath = Path.Combine(dir, "scheme.json");
            if (!File.Exists(jsonPath)) return false;

            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(jsonPath));
            if (root == null) return false;

            object cursors;
            if (root.TryGetValue("cursors", out cursors))
            {
                var byRole = cursors as Dictionary<string, object>;
                if (byRole != null)
                {
                    foreach (var role in Roles.Order)
                    {
                        object rel;
                        if (!byRole.TryGetValue(role, out rel) || rel == null) continue;
                        string full = rel.ToString();
                        if (full.Length == 0) continue;
                        if (!Path.IsPathRooted(full)) full = Path.Combine(dir, full);
                        if (File.Exists(full)) map[role] = Path.GetFullPath(full);
                    }
                }
            }

            object display;
            if (root.TryGetValue("name", out display) && display != null && display.ToString().Length > 0)
                name = display.ToString();

            return map.Count > 0;
        }

        /// <summary>Multe pachete descarcate vin cu un Install.inf care contine maparea exacta.</summary>
        private static bool ResolveByInf(string dir, FileInfo[] files, Dictionary<string, string> map, ref string name)
        {
            var infs = new DirectoryInfo(dir).GetFiles("*.inf");
            if (infs.Length == 0) return false;

            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string schemeLine = null, schemeNameKey = null, section = "";

            foreach (var line in File.ReadAllLines(infs[0].FullName))
            {
                string t = line.Trim();
                var sec = Regex.Match(t, @"^\[(.+)\]$");
                if (sec.Success) { section = sec.Groups[1].Value.ToLowerInvariant(); continue; }

                if (section == "strings")
                {
                    var kv = Regex.Match(t, "^\\s*([A-Za-z0-9_]+)\\s*=\\s*\"?([^\"]*?)\"?\\s*$");
                    if (kv.Success) strings[kv.Groups[1].Value] = kv.Groups[2].Value;
                }
                else if (t.IndexOf(@"Control Panel\Cursors\Schemes", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var tail = Regex.Match(t, ",\\s*\"([^\"]+)\"\\s*$");
                    if (!tail.Success) continue;
                    schemeLine = tail.Groups[1].Value;
                    var nm = Regex.Match(t, "\"%([A-Za-z0-9_]+)%\"\\s*,\\s*0x");
                    if (nm.Success) schemeNameKey = nm.Groups[1].Value;
                }
            }
            if (schemeLine == null) return false;

            for (int i = 0; i < 3; i++)
                schemeLine = Regex.Replace(schemeLine, "%([A-Za-z0-9_]+)%", m =>
                {
                    string v;
                    return strings.TryGetValue(m.Groups[1].Value, out v) ? v : m.Value;
                });

            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) byName[f.Name] = f.FullName;

            var parts = schemeLine.Split(',');
            for (int i = 0; i < Roles.Order.Length && i < parts.Length; i++)
            {
                string leaf = Path.GetFileName(parts[i].Trim());
                string full;
                if (leaf.Length > 0 && byName.TryGetValue(leaf, out full)) map[Roles.Order[i]] = full;
            }
            if (map.Count == 0) return false;

            string display;
            if (schemeNameKey != null && strings.TryGetValue(schemeNameKey, out display) && display.Length > 0)
                name = display;
            return true;
        }

        /// <summary>
        /// Install.inf-urile din pachete nu sunt de incredere: macOS (ful1e5) isi listeaza fisierele
        /// in alta ordine decat cea a rolurilor, Material Design inverseaza Pin/Person - Windows le-ar
        /// instala la fel de amestecat. Un nume de fisier fara echivoc ("Unavailable", "Vert", "Pin")
        /// bate deci maparea din INF; INF-ul ramane pentru rolurile pe care numele nu le lamureste.
        /// </summary>
        private static void CorrectByFileName(FileInfo[] files, Dictionary<string, string> map)
        {
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            ResolveByFileName(files, byName, Tier1);
            var claimed = new HashSet<string>(byName.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var role in Roles.Order)
            {
                string file;
                if (byName.TryGetValue(role, out file)) map[role] = file;
                // Un fisier revendicat de alt rol prin nume nu mai poate sta si aici.
                else if (map.TryGetValue(role, out file) && claimed.Contains(file)) map.Remove(role);
            }
        }

        private static Scheme FromFolder(string dir)
        {
            // Fisierele din radacina au prioritate; coboram in subfoldere doar daca radacina e goala,
            // altfel un subfolder de "bonus cursors" fura roluri de la setul principal.
            var files = new DirectoryInfo(dir).GetFiles().Where(IsCursorFile).ToArray();
            if (files.Length == 0)
                files = new DirectoryInfo(dir).GetFiles("*", SearchOption.AllDirectories).Where(IsCursorFile).ToArray();
            if (files.Length == 0) return null;

            var scheme = new Scheme { FromLibrary = true, Location = dir };
            string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));

            // Prioritate: scheme.json (scris de mana) > Install.inf > ghicire dupa nume.
            if (!ResolveByJson(dir, scheme.Map, ref name))
            {
                if (ResolveByInf(dir, files, scheme.Map, ref name))
                    CorrectByFileName(files, scheme.Map);
                else
                    ResolveByFileName(files, scheme.Map);
            }

            if (scheme.Map.Count == 0) return null;
            scheme.Name = name;
            return scheme;
        }

        private static IEnumerable<Scheme> FromRegistry()
        {
            var result = new List<Scheme>();
            var keys = new[]
            {
                Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors\Schemes"),
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes")
            };

            foreach (var key in keys)
            {
                if (key == null) continue;
                using (key)
                {
                    foreach (var valueName in key.GetValueNames())
                    {
                        var raw = key.GetValue(valueName) as string;
                        if (string.IsNullOrEmpty(raw)) continue;

                        var scheme = new Scheme { Name = valueName, FromLibrary = false };
                        var parts = raw.Split(',');
                        for (int i = 0; i < Roles.Order.Length && i < parts.Length; i++)
                        {
                            string v = Environment.ExpandEnvironmentVariables(parts[i].Trim());
                            if (v.Length > 0) scheme.Map[Roles.Order[i]] = v;
                        }
                        if (scheme.Map.Count == 0) continue;
                        string first = Roles.Order.First(r => scheme.Map.ContainsKey(r));
                        scheme.Location = Path.GetDirectoryName(scheme.Map[first]);
                        result.Add(scheme);
                    }
                }
            }
            return result;
        }

        internal static List<Scheme> GetAll(string libraryDir)
        {
            var fromLibrary = new List<Scheme>();
            if (Directory.Exists(libraryDir))
            {
                foreach (var dir in Directory.GetDirectories(libraryDir))
                {
                    // ".import-*" = dezarhivare intrerupta; nu e o schema.
                    if (Path.GetFileName(dir).StartsWith(".")) continue;
                    var s = FromFolder(dir);
                    if (s != null) fromLibrary.Add(s);
                }
            }

            var taken = new HashSet<string>(fromLibrary.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            var fromRegistry = FromRegistry().Where(s => !taken.Contains(s.Name));

            var all = new List<Scheme>();
            all.AddRange(fromLibrary.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase));
            all.AddRange(fromRegistry.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase));
            return all;
        }
    }

    internal static class CursorConfig
    {
        private const string CursorsPath = @"Control Panel\Cursors";
        private const string SchemesPath = @"Control Panel\Cursors\Schemes";

        internal static string ActiveName()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(CursorsPath))
            {
                if (key == null) return "";
                return (key.GetValue("") as string) ?? "";
            }
        }

        internal static void BackupOnce(string backupFile)
        {
            if (File.Exists(backupFile)) return;
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var key = Registry.CurrentUser.OpenSubKey(CursorsPath))
            {
                foreach (var role in Roles.Order)
                    snapshot[role] = key == null ? "" : ((key.GetValue(role) as string) ?? "");
                snapshot["Scheme"] = key == null ? "" : ((key.GetValue("") as string) ?? "");
            }
            File.WriteAllText(backupFile, new JavaScriptSerializer().Serialize(snapshot));
        }

        private static void Write(Dictionary<string, string> values, string schemeName)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(CursorsPath))
            {
                foreach (var role in Roles.Order)
                {
                    string v;
                    if (!values.TryGetValue(role, out v)) v = "";
                    key.SetValue(role, v, RegistryValueKind.ExpandString);
                }
                key.SetValue("", schemeName ?? "");
            }
            Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, Native.SPIF_UPDATEINIFILE_SENDCHANGE);
        }

        internal static void Apply(Scheme scheme, string backupFile)
        {
            BackupOnce(backupFile);
            Write(scheme.Map, scheme.Name);

            // Inregistram schema si in lista Windows, ca sa apara in Proprietati mouse.
            var ordered = Roles.Order.Select(r => scheme.Map.ContainsKey(r) ? scheme.Map[r] : "");
            using (var key = Registry.CurrentUser.CreateSubKey(SchemesPath))
                key.SetValue(scheme.Name, string.Join(",", ordered.ToArray()), RegistryValueKind.ExpandString);
        }

        internal static void Restore(string backupFile)
        {
            var saved = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(backupFile));
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var role in Roles.Order)
            {
                string v;
                values[role] = saved.TryGetValue(role, out v) ? (v ?? "") : "";
            }
            string scheme;
            Write(values, saved.TryGetValue("Scheme", out scheme) ? scheme : "");
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly string _libraryDir;
        private readonly string _backupFile;

        private readonly SchemeList _list = new SchemeList();
        private readonly CardPanel _flow = new CardPanel();
        private readonly TrackedLabel _title = new TrackedLabel();
        private readonly Label _subtitle = new Label();
        private readonly Label _count = new Label();
        private readonly Label _status = new Label();
        private readonly FlatButton _apply = new FlatButton("Apply selected scheme", true);
        private readonly FlatButton _restore = new FlatButton("Restore saved cursors", false);
        private readonly Timer _timer = new Timer();

        private readonly Dictionary<string, PreviewBox> _boxes = new Dictionary<string, PreviewBox>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _labels = new Dictionary<string, Label>(StringComparer.Ordinal);
        private List<IntPtr> _liveHandles = new List<IntPtr>();
        private int _hoverIndex = -1;
        private string _activeName = "";

        internal MainForm()
        {
            string root = Path.GetDirectoryName(Application.ExecutablePath);
            _libraryDir = Path.Combine(root, "Library");
            _backupFile = Path.Combine(root, "backup.json");
            if (!Directory.Exists(_libraryDir)) Directory.CreateDirectory(_libraryDir);

            Text = "Cursor Selector";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Size = new Size(1240, 840);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.FontBody;
            BackColor = Theme.Ground;

            BuildLeftPanel();
            BuildRightPanel();
            BuildTiles();

            _timer.Interval = 90;
            _timer.Tick += delegate
            {
                foreach (var box in _boxes.Values) box.Advance();
            };

            // Un folder sau un .zip lasat oriunde pe fereastra. Evenimentele de drop nu urca din
            // controalele copil, asa ca le legam pe fiecare.
            EnableDrop(this);

            Shown += delegate { RefreshList(null); _timer.Start(); };
            FormClosed += delegate
            {
                _timer.Stop();
                foreach (var h in _liveHandles) Native.DestroyCursor(h);
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Bara de titlu intunecata: atributul 20 pe Windows 11, 19 pe versiuni mai vechi.
            int on = 1;
            if (Native.DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));

            int round = 2;                                   // DWMWCP_ROUND
            Native.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));

            // Atributul de bordura vrea 0x00BBGGRR, nu ordinea din hexul de paleta.
            int border = Theme.Edge.B << 16 | Theme.Edge.G << 8 | Theme.Edge.R;
            Native.DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
        }

        private void BuildLeftPanel()
        {
            var left = new Panel
            {
                Dock = DockStyle.Left,
                Width = 340,
                Padding = new Padding(16, 16, 10, 16),
                BackColor = Theme.Ground2
            };

            _list.Dock = DockStyle.Fill;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 40;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Ground2;
            _list.DrawItem += DrawSchemeRow;
            _list.SelectedIndexChanged += delegate { ShowScheme(_list.SelectedItem as Scheme); };
            _list.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int index = _list.IndexFromPoint(e.Location);
                if (index != _hoverIndex) { InvalidateRow(_hoverIndex); _hoverIndex = index; InvalidateRow(index); }
            };
            _list.MouseLeave += delegate
            {
                if (_hoverIndex != -1) { int old = _hoverIndex; _hoverIndex = -1; InvalidateRow(old); }
            };
            _list.HandleCreated += delegate { Native.SetWindowTheme(_list.Handle, "DarkMode_Explorer", null); };

            var head = new Rail("Library") { Dock = DockStyle.Top, BackColor = Theme.Ground2 };

            _count.Dock = DockStyle.Top;
            _count.Height = 28;
            _count.Font = Theme.FontMono;          // cifrele in mono, altfel lista citeste a text rasfirat
            _count.ForeColor = Theme.Dim;
            _count.BackColor = Color.Transparent;
            _count.AutoSize = false;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 176,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Ground2,
                Padding = new Padding(0, 10, 0, 0)
            };

            var import = new FlatButton("Import and apply\u2026", false);
            var openLib = new FlatButton("Open Library folder", false);
            var refresh = new FlatButton("Refresh list", false);
            foreach (var b in new[] { import, openLib, refresh })
            {
                b.Size = new Size(312, 38);
                b.Margin = new Padding(0, 0, 0, 8);
            }
            import.Click += OnImport;
            openLib.Click += delegate { System.Diagnostics.Process.Start("explorer.exe", _libraryDir); };
            refresh.Click += delegate { RefreshList(null); };

            var dropHint = new Label
            {
                Text = "or drop a folder / .zip on the window",
                Size = new Size(312, 24),
                Margin = new Padding(0),
                Font = Theme.FontSmall,
                ForeColor = Theme.Dim,
                TextAlign = ContentAlignment.TopCenter
            };
            buttons.Controls.AddRange(new Control[] { dropHint, import, openLib, refresh });

            // Controlul cu Dock=Fill trebuie sa fie in fata, ca sa primeasca spatiul ramas.
            left.Controls.Add(_list);
            left.Controls.Add(_count);
            left.Controls.Add(head);
            left.Controls.Add(buttons);
            _list.BringToFront();
            Controls.Add(left);
        }

        /// <summary>
        /// Redesenam doar randul atins si fara stergerea fundalului: Invalidate() pe tot ListBox-ul
        /// il golea intai cu fundalul si apoi redesena fiecare rand, de unde flicker-ul la hover.
        /// DrawSchemeRow umple singur tot dreptunghiul randului, deci stergerea nu lipseste nimanui.
        /// </summary>
        private void InvalidateRow(int index)
        {
            if (index < 0 || index >= _list.Items.Count) return;
            var r = _list.GetItemRectangle(index);
            var rect = new Native.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
            Native.InvalidateRect(_list.Handle, ref rect, false);
        }

        private void DrawSchemeRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            // Randul se compune intai off-screen si ajunge pe ecran dintr-o bucata; desenat direct,
            // fundalul apare o clipa fara text la fiecare repaint.
            // Bitmap desenat de la 0,0: TextRenderer ignora originea mutata a unui BufferedGraphics.
            if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
            using (var bitmap = new Bitmap(e.Bounds.Width, e.Bounds.Height))
            {
                using (var g = Graphics.FromImage(bitmap))
                    DrawSchemeRow(g, new DrawItemEventArgs(g, e.Font,
                        new Rectangle(Point.Empty, e.Bounds.Size), e.Index, e.State));
                e.Graphics.DrawImageUnscaled(bitmap, e.Bounds.Location);
            }
        }

        private void DrawSchemeRow(Graphics g, DrawItemEventArgs e)
        {
            var scheme = (Scheme)_list.Items[e.Index];
            // Nu folosim Graphics.Clear: umple toata suprafata controlului, nu doar randul curent.
            using (var background = new SolidBrush(Theme.Ground2))
                g.FillRectangle(background, e.Bounds);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4);
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if (selected)
            {
                // Pe un rand lat, o bordura de accent de 1px ar citi ca input focusat; identitatea
                // cere in schimb tenta de accent plus o bara pe muchia din fata.
                Theme.FillRounded(g, r, 10, Theme.SelectedRow);
                using (var brush = new SolidBrush(Theme.Accent))
                using (var path = Theme.RoundedPath(new Rectangle(r.X, r.Y + 7, 3, r.Height - 14), 2))
                    g.FillPath(brush, path);
            }
            else if (e.Index == _hoverIndex)
            {
                Theme.FillRounded(g, r, 10, Theme.PanelOnSide);
            }

            const float tracking = 0.9f;
            string badgeText = scheme.SourceLabel.ToUpperInvariant();
            int badgeWidth = Theme.MeasureTracked(g, badgeText, Theme.FontBadge, tracking);
            int badgeHeight = Theme.LineHeight(g, Theme.FontBadge);
            var badge = new Rectangle(r.Right - badgeWidth - 20, r.Y + (r.Height - badgeHeight - 6) / 2,
                                      badgeWidth + 15, badgeHeight + 6);
            Theme.DrawRounded(g, badge, badge.Height / 2,
                scheme.FromLibrary ? Color.FromArgb(150, Theme.Accent) : Theme.EdgeBright);
            Theme.DrawTracked(g, badgeText, Theme.FontBadge, new Point(badge.X + 7, badge.Y + 3),
                scheme.FromLibrary ? Theme.Accent : Theme.Dim, tracking);

            // Schema aplicata acum: punct de accent in fata numelui, acelasi semn ca in rail.
            if (scheme.Name == _activeName)
                using (var brush = new SolidBrush(Theme.Accent))
                    g.FillEllipse(brush, r.X + 10, r.Y + r.Height / 2 - 3, 6, 6);

            var nameRect = new Rectangle(r.X + 24, r.Y, badge.X - r.X - 30, r.Height);
            TextRenderer.DrawText(g, scheme.Name, Theme.FontBody, nameRect,
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void BuildRightPanel()
        {
            var right = new ComposedPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 16, 20, 16),
                BackColor = Theme.Ground
            };

            _title.Dock = DockStyle.Top;
            _title.Height = 44;
            _title.Text = "Select a scheme";
            _title.Font = Theme.FontDisplay;
            _title.Ink = Theme.Text;
            _title.Tracking = -0.4f;               // -0.02em la 15pt
            _title.BackColor = Theme.Ground;

            _subtitle.Dock = DockStyle.Top;
            _subtitle.Height = 28;
            _subtitle.Font = Theme.FontMono;       // cifre si cai: mono
            _subtitle.ForeColor = Theme.Dim;
            _subtitle.BackColor = Color.Transparent;
            _subtitle.AutoSize = false;
            _subtitle.AutoEllipsis = true;

            var rail = new Rail("Cursor roles") { Dock = DockStyle.Top, BackColor = Theme.Ground };

            _flow.Dock = DockStyle.Fill;
            _flow.AutoScroll = true;
            _flow.Padding = new Padding(16, 14, 16, 14);
            _flow.HandleCreated += delegate { Native.SetWindowTheme(_flow.Handle, "DarkMode_Explorer", null); };

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 68,
                BackColor = Theme.Ground
            };

            // Pilula butonului principal e retrasa cu 7px in interiorul controlului, ca sa aiba loc
            // haloul; de aceea controlul incepe la -7, ca muchia vizibila sa se alinieze cu cardul.
            _apply.SetBounds(-7, 8, 228, 52);
            _apply.Click += OnApply;

            _restore.SetBounds(226, 15, 224, 38);
            _restore.Click += OnRestore;

            _status.Dock = DockStyle.Right;
            _status.Width = 240;
            _status.Font = Theme.FontSmall;
            _status.TextAlign = ContentAlignment.MiddleRight;
            _status.ForeColor = Theme.Dim;
            _status.BackColor = Color.Transparent;
            _status.AutoSize = false;
            _status.AutoEllipsis = true;

            bottom.Controls.Add(_apply);
            bottom.Controls.Add(_restore);
            bottom.Controls.Add(_status);

            right.Controls.Add(_flow);
            right.Controls.Add(rail);
            right.Controls.Add(_subtitle);
            right.Controls.Add(_title);
            right.Controls.Add(bottom);
            _flow.BringToFront();

            Controls.Add(right);
            right.BringToFront();
        }

        private void BuildTiles()
        {
            foreach (var role in Roles.Order)
            {
                var tile = new Panel
                {
                    Size = new Size(150, 136),
                    Margin = new Padding(4),
                    BackColor = Theme.Panel
                };

                var box = new PreviewBox
                {
                    Bounds = new Rectangle(0, 0, 150, 92),
                    DrawSize = 64
                };

                var label = new Label
                {
                    Bounds = new Rectangle(0, 98, 150, 38),
                    Text = Roles.Display[role],
                    Font = Theme.FontSmall,
                    TextAlign = ContentAlignment.TopCenter,
                    ForeColor = Theme.Text,
                    BackColor = Color.Transparent,
                    AutoSize = false
                };

                tile.Controls.Add(label);
                tile.Controls.Add(box);
                _flow.Controls.Add(tile);
                _boxes[role] = box;
                _labels[role] = label;
            }
        }

        private void ShowScheme(Scheme scheme)
        {
            var previous = _liveHandles;
            var current = new List<IntPtr>();

            foreach (var role in Roles.Order)
            {
                string path = null;
                if (scheme != null) scheme.Map.TryGetValue(role, out path);

                IntPtr handle = IntPtr.Zero;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    handle = Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_CURSOR, 64, 64, Native.LR_LOADFROMFILE);
                if (handle == IntPtr.Zero && !string.IsNullOrEmpty(path) && File.Exists(path))
                    handle = Native.LoadCursorFromFile(path);   // unele .ani vechi nu trec prin LoadImage
                if (handle != IntPtr.Zero) current.Add(handle);

                _boxes[role].Reset(handle);
                _labels[role].ForeColor = path == null ? Theme.Dimmer : Theme.Text;
            }

            // Fiecare incarcare creeaza un handle nou si procesul are o limita (~300 cursoare
            // simultan), asa ca eliberam schema anterioara acum ca placile refera handle-urile noi.
            _liveHandles = current;
            foreach (var h in previous) Native.DestroyCursor(h);

            if (scheme == null)
            {
                _title.Text = "Select a scheme";
                _subtitle.Text = "";
                return;
            }

            _title.Text = scheme.Name;
            int missing = Roles.Order.Length - scheme.Map.Count;
            // Calea la urma: cand randul nu incape, ea e cea care se trunchiaza,
            // nu numaratoarea si nici avertismentul despre rolurile nemapate.
            _subtitle.Text = string.Format("{0}/{1} cursors  \u00b7  {2}{3}",
                scheme.Map.Count, Roles.Order.Length,
                missing > 0 ? string.Format("{0} unset, Windows default  \u00b7  ", missing) : "",
                scheme.Location);
        }

        /// <summary>Reciteste biblioteca; selecteaza schema din <paramref name="location"/> daca e data.</summary>
        private void RefreshList(string location)
        {
            var previous = _list.SelectedItem as Scheme;
            string previousName = previous == null ? null : previous.Name;

            _list.BeginUpdate();
            _list.Items.Clear();
            int library = 0;
            foreach (var scheme in Library.GetAll(_libraryDir))
            {
                _list.Items.Add(scheme);
                if (scheme.FromLibrary) library++;
            }
            _list.EndUpdate();
            Native.SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);

            _count.Text = string.Format("{0} schemes  \u00b7  {1} library  \u00b7  {2} system",
                _list.Items.Count, library, _list.Items.Count - library);
            SetActive(CursorConfig.ActiveName());
            _restore.Enabled = File.Exists(_backupFile);

            if (_list.Items.Count == 0) { ShowScheme(null); return; }

            int index = 0;
            for (int i = 0; i < _list.Items.Count; i++)
            {
                var s = (Scheme)_list.Items[i];
                if (location != null
                        ? string.Equals(s.Location, location, StringComparison.OrdinalIgnoreCase)
                        : s.Name == previousName)
                { index = i; break; }
            }
            _list.SelectedIndex = index;
        }

        private void SetActive(string name)
        {
            _activeName = name;
            _status.Text = "Active:  " + name;
            _list.Invalidate();
        }

        private void OnApply(object sender, EventArgs e)
        {
            var scheme = _list.SelectedItem as Scheme;
            if (scheme == null) return;
            try
            {
                CursorConfig.Apply(scheme, _backupFile);
                SetActive(scheme.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not apply the scheme:\n" + ex.Message,
                    "Cursor Selector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _restore.Enabled = File.Exists(_backupFile);
        }

        private void OnRestore(object sender, EventArgs e)
        {
            if (!File.Exists(_backupFile)) return;
            try
            {
                CursorConfig.Restore(_backupFile);
                SetActive(CursorConfig.ActiveName());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not restore:\n" + ex.Message,
                    "Cursor Selector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnImport(object sender, EventArgs e)
        {
            // Dialog de fisiere, nu de foldere: acopera cu o singura alegere si arhiva descarcata,
            // si pachetul deja dezarhivat (orice fisier din el), cu dialogul modern din Explorer.
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Pick a .zip, or any cursor file inside the pack's folder";
                dialog.Filter = "Cursor pack (*.zip, *.inf, *.cur, *.ani)|*.zip;*.inf;*.cur;*.ani";
                dialog.InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string picked = dialog.FileName;
                ImportAndApply(picked.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? picked : Path.GetDirectoryName(picked));
            }
        }

        private void EnableDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += delegate(object s, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            control.DragDrop += delegate(object s, DragEventArgs e)
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0) return;
                string path = paths[0];
                // Un fisier dintr-un pachet dezarhivat inseamna folderul lui, la fel ca in dialog.
                if (File.Exists(path) && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    path = Path.GetDirectoryName(path);
                // Drop-ul ruleaza in bucla OLE a Explorer-ului; dialogurile se deschid dupa ce se incheie.
                BeginInvoke((Action)(() => ImportAndApply(path)));
            };
            foreach (Control child in control.Controls) EnableDrop(child);
        }

        private static bool HasCursors(string dir)
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Any(f => f.EndsWith(".cur", StringComparison.OrdinalIgnoreCase)
                       || f.EndsWith(".ani", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Arhivele au des un singur folder in radacina ("Pack\Pack\*.cur"). Coboram pana la primul
        /// nivel care chiar are fisiere, ca Install.inf si scheme.json sa fie gasite unde le cauta Library.
        /// </summary>
        private static string Unwrap(string dir)
        {
            while (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 1)
                dir = Directory.GetDirectories(dir)[0];
            return dir;
        }

        /// <summary>
        /// Copiaza pachetul (folder sau .zip) in Library, apoi il selecteaza si il aplica. Copia face
        /// schema independenta de sursa: userul poate sterge descarcarea fara sa piarda cursoarele.
        /// </summary>
        private void ImportAndApply(string path)
        {
            bool isZip = File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (!isZip && !Directory.Exists(path)) return;

            string libraryRoot = Path.GetFullPath(_libraryDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string destination;
            string staging = null;

            try
            {
                Cursor = Cursors.WaitCursor;
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

                if (!isZip && full.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
                {
                    // Deja in biblioteca: nimic de copiat, doar urcam la folderul schemei.
                    destination = full;
                    while (!string.Equals(Path.GetDirectoryName(destination) + Path.DirectorySeparatorChar,
                                          libraryRoot, StringComparison.OrdinalIgnoreCase))
                        destination = Path.GetDirectoryName(destination);
                }
                else
                {
                    string source;
                    if (isZip)
                    {
                        // Dezarhivam langa biblioteca (acelasi volum), ca mutarea finala sa fie un rename.
                        // ExtractToDirectory refuza intrarile care ar iesi din folderul tinta.
                        staging = Path.Combine(_libraryDir, ".import-" + Guid.NewGuid().ToString("N"));
                        ZipFile.ExtractToDirectory(full, staging);
                        source = Unwrap(staging);
                    }
                    else
                    {
                        source = Unwrap(full);
                    }

                    if (!HasCursors(source))
                    {
                        MessageBox.Show(this, "No .cur or .ani files in '" + Path.GetFileName(full) + "'.",
                            "Cursor Selector", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    // Arhiva fara folder in radacina ia numele fisierului .zip.
                    string name = source == staging ? Path.GetFileNameWithoutExtension(full) : Path.GetFileName(source);
                    destination = Path.Combine(_libraryDir, name);

                    if (Directory.Exists(destination))
                    {
                        // Acelasi nume deja importat: il refolosim, nu suprascriem ce e acolo.
                    }
                    else if (isZip)
                    {
                        Directory.Move(source, destination);
                    }
                    else
                    {
                        // Dialogul de copiere din shell: pachetele mari dureaza si merita bara de progres.
                        Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(source, destination,
                            Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Arhiva corupta, fisiere blocate, disc plin: toate reale la un import.
                MessageBox.Show(this, "Could not import:\n" + ex.Message,
                    "Cursor Selector", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
                if (staging != null && Directory.Exists(staging))
                    try { Directory.Delete(staging, true); } catch (IOException) { }
            }

            RefreshList(destination);
            var scheme = _list.SelectedItem as Scheme;
            if (scheme == null || !string.Equals(scheme.Location, destination, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Imported, but no cursor roles could be matched in '" + Path.GetFileName(destination) + "'.",
                    "Cursor Selector", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OnApply(this, EventArgs.Empty);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Native.SetProcessDPIAware();
            Native.TryEnableDarkControls();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
