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

// Fara ele, exe-ul nu spunea al cui e (FileVersion 0.0.0.0, fara companie sau descriere): semnul
// static cel mai des pus pe seama unui fisier anonim de motoarele antivirus cu euristici ML.
[assembly: System.Reflection.AssemblyTitle("Cursor Selector")]
[assembly: System.Reflection.AssemblyDescription("Cursor Selector - preview and apply Windows cursor schemes")]
[assembly: System.Reflection.AssemblyCompany("Protagonist Labs")]
[assembly: System.Reflection.AssemblyProduct("Cursor Selector")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 Protagonist Labs. MIT licence")]
[assembly: System.Reflection.AssemblyVersion("1.4.1")]
[assembly: System.Reflection.AssemblyFileVersion("1.4.1")]

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, string l);
        internal const uint EM_SETCUEBANNER = 0x1501;

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
        internal static readonly Color Violet = Color.FromArgb(0xB1, 0x5C, 0xFF);   // doar in aura
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

        /// <summary>
        /// Fonturile marcii, inglobate in executabil (vezi build.cmd): pe o masina straina nu e
        /// instalat nici Bricolage, nici IBM Plex, deci fara ele aplicatia cadea pe Segoe UI si
        /// arata ca orice alt program WinForms.
        ///
        /// Se incarca de doua ori, fiindca cele doua motoare de text ale Windows-ului au fiecare
        /// lista lui: PrivateFontCollection pentru GDI+ (de acolo vin obiectele Font), iar
        /// AddFontMemResourceEx pentru GDI, pe care il foloseste TextRenderer - fara a doua
        /// inregistrare, tot ce deseneaza TextRenderer ar reveni la fontul implicit. Memoria ramane
        /// alocata cat traieste procesul: fonturile se citesc din ea la fiecare desenare.
        /// </summary>
        private static class Embedded
        {
            [DllImport("gdi32.dll", ExactSpelling = true)]
            private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, out uint count);

            private static readonly System.Drawing.Text.PrivateFontCollection Collection = Load();

            private static System.Drawing.Text.PrivateFontCollection Load()
            {
                var collection = new System.Drawing.Text.PrivateFontCollection();
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = assembly.GetManifestResourceStream(name))
                    {
                        var bytes = new byte[stream.Length];
                        int read = 0;
                        while (read < bytes.Length)
                        {
                            int got = stream.Read(bytes, read, bytes.Length - read);
                            if (got <= 0) break;
                            read += got;
                        }
                        if (read != bytes.Length) continue;

                        // Tabloul ramane fixat pe loc, nu copiat in memorie nativa: "resursa copiata in
                        // memorie alocata nativ" e tiparul static al incarcatoarelor de cod, cautat de euristici.
                        IntPtr buffer = GCHandle.Alloc(bytes, GCHandleType.Pinned).AddrOfPinnedObject();
                        collection.AddMemoryFont(buffer, bytes.Length);
                        uint installed;
                        AddFontMemResourceEx(buffer, (uint)bytes.Length, IntPtr.Zero, out installed);
                    }
                }
                return collection;
            }

            internal static FontFamily Find(string name, FontStyle style)
            {
                foreach (var family in Collection.Families)
                    if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase)
                        && family.IsStyleAvailable(style)) return family;
                return null;
            }
        }

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
            {
                var embedded = Embedded.Find(name, style);
                if (embedded != null) return new Font(embedded, size, style);
                // Instalata de utilizator: o preferam inaintea urmatoarei din teanc, nu a celei inglobate.
                if (Installed.Contains(name)) return new Font(name, size, style);
            }
            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        internal static readonly Font FontDisplay = Pick(DisplayStack, 18F, FontStyle.Bold);
        internal static readonly Font FontBrand = Pick(DisplayStack, 11.5F, FontStyle.Bold);
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

        /// <summary>
        /// Aura din identitate: doua pete radiale discrete in spatele continutului (accent
        /// stanga-sus, violet dreapta-sus). Nota zice ca daca se observa ca forma, e prea tare.
        /// </summary>
        internal static void DrawAura(Graphics g, Rectangle area)
        {
            Blob(g, area, 0.22f, -0.04f, 0.58f, 0.52f, Accent, 46);
            Blob(g, area, 0.80f, 0.04f, 0.46f, 0.44f, Violet, 30);
        }

        private static void Blob(Graphics g, Rectangle area, float cx, float cy, float rx, float ry, Color color, int alpha)
        {
            var box = new Rectangle(
                area.X + (int)(area.Width * (cx - rx)), area.Y + (int)(area.Height * (cy - ry)),
                (int)(area.Width * rx * 2), (int)(area.Height * ry * 2));
            if (box.Width <= 0 || box.Height <= 0) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(box);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(alpha, color);
                    brush.SurroundColors = new[] { Color.FromArgb(0, color) };
                    // Fara asta centrul e o minge compacta, nu o lumina care se stinge.
                    brush.FocusScales = new PointF(0.1f, 0.1f);
                    g.FillPath(brush, path);
                }
            }
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
    /// <summary>
    /// O placa de rol: cursorul sta pe o pastila gri cat el, nu pe o lespede - 17 lespezi deschise
    /// la culoare acopereau interfata intunecata. Placa deseneaza tot (fundal, pastila, cursor,
    /// eticheta), deci nu mai sunt trei controale suprapuse care se repicteaza unul peste altul.
    /// </summary>
    internal sealed class RoleTile : Control
    {
        internal const int W = 150, H = 112, Chip = 68, Draw = 52;

        internal readonly string Role;
        internal string File;
        internal IntPtr CursorHandle = IntPtr.Zero;
        private int _step;
        private bool _animated = true;
        private bool _hover;

        internal RoleTile(string role, string display)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Role = role;
            Text = display;
            Size = new Size(W, H);
            Margin = new Padding(3);
            BackColor = Theme.Ground;
            Cursor = Cursors.Hand;
        }

        internal void Reset(IntPtr handle, string file)
        {
            CursorHandle = handle;
            File = file;
            _step = 0;
            _animated = true;
            Invalidate();
        }

        internal void Advance()
        {
            if (_animated && CursorHandle != IntPtr.Zero) { _step++; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var tile = new Rectangle(0, 0, Width - 1, Height - 1);
            bool empty = CursorHandle == IntPtr.Zero;

            Theme.FillRounded(g, tile, Theme.Radius, _hover ? Theme.Panel2 : Theme.Panel);
            Theme.DrawRounded(g, tile, Theme.Radius, _hover ? Theme.EdgeBright : Theme.Edge);

            var chip = new Rectangle((Width - Chip) / 2, 10, Chip, Chip);
            Theme.FillRounded(g, chip, 20, empty ? Theme.SwatchEmpty : Theme.Swatch);
            g.SmoothingMode = SmoothingMode.Default;

            var label = new Rectangle(4, chip.Bottom + 8, Width - 8, Height - chip.Bottom - 10);
            TextRenderer.DrawText(g, Text, Theme.FontSmall, label, empty ? Theme.Dimmer : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);

            if (empty)
            {
                TextRenderer.DrawText(g, "\u2014", Theme.FontBody, chip, Theme.Dimmer,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int x = chip.X + (Chip - Draw) / 2, y = chip.Y + (Chip - Draw) / 2;
            IntPtr hdc = g.GetHdc();
            bool ok = Native.DrawIconEx(hdc, x, y, CursorHandle, Draw, Draw, _step, IntPtr.Zero, Native.DI_NORMAL);
            g.ReleaseHdc(hdc);

            // Cursor static: pasul 1 esueaza, revenim la cadrul 0 si oprim animatia.
            if (!ok && _step != 0) { _step = 0; _animated = false; Invalidate(); }
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
                   | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
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
                   | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
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
        /// <summary>Ce scrie in card cand nu e nimic de aratat; null cat timp sunt placi.</summary>
        internal string EmptyMessage;
        internal string EmptyHint;

        internal CardPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Ground;
        }

        internal void SetEmpty(string message, string hint)
        {
            EmptyMessage = message;
            EmptyHint = hint;
            foreach (Control child in Controls) child.Visible = message == null;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Ground);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(g, r, Theme.RadiusLarge, Theme.Panel);
            Theme.DrawRounded(g, r, Theme.RadiusLarge, Theme.Edge);
            if (EmptyMessage == null) return;

            // Chenar punctat: acelasi semn ca pe web pentru "lasa ceva aici".
            var drop = new Rectangle(r.X + 40, r.Y + 40, r.Width - 80, r.Height - 80);
            using (var pen = new Pen(Theme.Edge, 1.4f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 6f, 5f } })
            using (var path = Theme.RoundedPath(drop, Theme.RadiusLarge))
                g.DrawPath(pen, path);

            var middle = new Rectangle(drop.X, drop.Y + drop.Height / 2 - 40, drop.Width, 40);
            TextRenderer.DrawText(g, EmptyMessage, Theme.FontDisplay, middle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
            TextRenderer.DrawText(g, EmptyHint, Theme.FontBody,
                new Rectangle(drop.X, middle.Bottom + 10, drop.Width, 28), Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }
    }

    /// <summary>
    /// ListBox fara stergerea fundalului sub randuri: fiecare rand isi umple singur dreptunghiul,
    /// deci stergerea de dinainte era doar un cadru gol vizibil (flicker). Umplem doar zona de sub
    /// ultimul rand, pe care nu o picteaza nimeni altcineva.
    /// </summary>
    /// <summary>
    /// Campul de cautare: un TextBox fara chenar, asezat intr-un panou care deseneaza pastila si
    /// lupa. Un TextBox nu poate avea coltul rotunjit, iar chenarul lui de sistem e singurul
    /// dreptunghi palid dintr-o bara altfel intunecata.
    /// </summary>
    /// <summary>
    /// Meniul de context, in paleta aplicatiei. ToolStrip-ul de sistem vine alb, iar nota de
    /// branding spune ca un control nativ nestilizat e cel mai vizibil loc unde se rupe tema.
    /// </summary>
    /// <summary>
    /// Antetul barei: semnul aplicatiei, numele si versiunea. Fereastra nu spunea nicaieri
    /// al cui produs e - in afara de bara de titlu a Windows-ului.
    /// </summary>
    internal sealed class BrandHead : Control
    {
        internal static readonly string Version = typeof(BrandHead).Assembly.GetName().Version.ToString(3);

        internal BrandHead()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 46;
        }

        private static readonly Bitmap Mark = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Ground2);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Marca: iconita aplicatiei (cea din exe), ca in bara de titlu si pe pagina.
            var mark = new Rectangle(0, (Height - 30) / 2, 30, 30);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Mark, mark);

            int line = Theme.LineHeight(g, Theme.FontBrand);
            TextRenderer.DrawText(g, "Cursor Selector", Theme.FontBrand,
                new Point(mark.Right + 12, Height / 2 - line + 2), Theme.Text, TextFormatFlags.NoPadding);
            Theme.DrawTracked(g, Version, Theme.FontBadge,
                new Point(mark.Right + 13, Height / 2 + 3), Theme.Dimmer, 0.8f);
        }
    }

    internal sealed class DarkMenu : ToolStripRenderer
    {
        internal static ContextMenuStrip Build()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenu(),
                BackColor = Theme.Ground2,
                ForeColor = Theme.Text,
                Font = Theme.FontBody,
                ShowImageMargin = false
            };
            return menu;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.Ground2);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawRounded(e.Graphics, new Rectangle(0, 0, e.AffectedBounds.Width - 1,
                e.AffectedBounds.Height - 1), 10, Theme.Edge);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRounded(e.Graphics, new Rectangle(3, 0, e.Item.Width - 6, e.Item.Height - 1), 8, Theme.SelectedRow);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dimmer;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Edge))
                e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
    }

    internal sealed class SearchBox : Panel
    {
        internal readonly TextBox Field = new TextBox();

        internal SearchBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 36;
            BackColor = Theme.Ground2;

            Field.BorderStyle = BorderStyle.None;
            Field.BackColor = Theme.Panel;
            Field.ForeColor = Theme.Text;
            Field.Font = Theme.FontBody;
            Field.HandleCreated += delegate
            {
                Native.SendMessage(Field.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, "Search schemes");
                Native.SetWindowTheme(Field.Handle, "DarkMode_CFD", null);
            };
            Controls.Add(Field);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int height = Field.PreferredHeight;
            Field.SetBounds(38, (Height - height) / 2, Math.Max(10, Width - 50), height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Ground2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(g, box, Height / 2, Theme.Panel);
            Theme.DrawRounded(g, box, Height / 2, Field.Focused ? Theme.Accent : Theme.Edge);

            using (var pen = new Pen(Field.Focused ? Theme.Dim : Theme.Dimmer, 1.6f))
            {
                var lens = new Rectangle(15, Height / 2 - 6, 11, 11);
                g.DrawEllipse(pen, lens);
                g.DrawLine(pen, lens.Right - 2, lens.Bottom - 2, lens.Right + 2, lens.Bottom + 2);
            }
        }
    }

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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Ground);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawAura(e.Graphics, new Rectangle(0, 0, Width, Height / 2));
        }

        protected override void OnResize(EventArgs e) { Invalidate(); base.OnResize(e); }
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

        // Grupurile din interfata: 17 roluri intr-un bloc se citeau ca o lista fara cap, iar randul
        // de la final ramanea ciuntit. Order de mai sus ramane ordinea canonica, cea din registru.
        internal static readonly string[][] Groups =
        {
            new[] { "Pointing", "Arrow", "UpArrow", "Hand", "Help" },
            new[] { "Writing",  "IBeam", "NWPen", "Crosshair" },
            new[] { "Status",   "Wait", "AppStarting", "No" },
            new[] { "Sizing",   "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll" },
            new[] { "Extras",   "Pin", "Person" }
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
        internal const string CursorsPath = Env.Reg + @"Control Panel\Cursors";
        internal const string SchemesPath = Env.Reg + @"Control Panel\Cursors\Schemes";

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
                values[role] = saved.TryGetValue(role, out v) ? DataFolder.Repoint(v ?? "") : "";
            }
            string scheme;
            Write(values, saved.TryGetValue("Scheme", out scheme) ? scheme : "");
        }
    }

    /// <summary>
    /// Fereastra unui singur cursor: desenat mare, cu fisierul din spatele lui. Pana aici,
    /// o placa de 64px era tot ce se putea vedea dintr-un cursor inainte sa-l aplici.
    /// </summary>
    internal sealed class CursorDetail : Form
    {
        private readonly IntPtr _handle;
        private readonly Timer _timer = new Timer();
        private int _step;
        private bool _animated = true;

        internal CursorDetail(string role, string file, IntPtr handle, int frames)
        {
            _handle = handle;
            Text = role;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 340);
            BackColor = Theme.Ground2;
            Font = Theme.FontBody;
            ShowInTaskbar = false;

            var name = new TrackedLabel
            {
                Text = role, Font = Theme.FontDisplay, Ink = Theme.Text, Tracking = -0.4f,
                Bounds = new Rectangle(24, 186, 372, 34)
            };
            var lines = new Label
            {
                Bounds = new Rectangle(24, 222, 372, 70),
                Font = Theme.FontMono,
                ForeColor = Theme.Dim,
                BackColor = Color.Transparent,
                AutoSize = false,
                Text = Describe(file, frames)
            };

            var close = new FlatButton("Close", false) { Bounds = new Rectangle(248, 292, 148, 36) };
            close.Click += delegate { Close(); };
            var folder = new FlatButton("Show the file", false) { Bounds = new Rectangle(24, 292, 160, 36) };
            folder.Enabled = file != null && System.IO.File.Exists(file);
            folder.Click += delegate
            {
                // /select, deschide folderul cu fisierul deja evidentiat.
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\"");
            };

            Controls.Add(name);
            Controls.Add(lines);
            Controls.Add(close);
            Controls.Add(folder);

            _timer.Interval = 90;
            _timer.Tick += delegate { if (_animated) { _step++; Invalidate(new Rectangle(0, 0, ClientSize.Width, 180)); } };
            _timer.Start();
            FormClosed += delegate { _timer.Stop(); };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private static string Describe(string file, int frames)
        {
            if (file == null || !System.IO.File.Exists(file)) return "no file - Windows default";
            var info = new System.IO.FileInfo(file);
            string kind = frames > 1 ? frames + " frames" : "single frame";
            return info.Name + "\n"
                 + (info.Length / 1024.0).ToString("0.#") + " KB  \u00b7  " + kind + "\n\n"
                 + info.DirectoryName;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            if (Native.DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            int round = 2;
            Native.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Ground2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawAura(g, new Rectangle(0, 0, ClientSize.Width, 180));

            var chip = new Rectangle((ClientSize.Width - 150) / 2, 22, 150, 150);
            Theme.FillRounded(g, chip, 34, _handle == IntPtr.Zero ? Theme.SwatchEmpty : Theme.Swatch);
            g.SmoothingMode = SmoothingMode.Default;
            if (_handle == IntPtr.Zero) return;

            IntPtr hdc = g.GetHdc();
            bool ok = Native.DrawIconEx(hdc, chip.X + 11, chip.Y + 11, _handle, 128, 128, _step, IntPtr.Zero, Native.DI_NORMAL);
            g.ReleaseHdc(hdc);
            if (!ok && _step != 0) { _step = 0; _animated = false; Invalidate(chip); }
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
        private readonly Timer _confirm = new Timer();
        private readonly Timer _updates = new Timer { Interval = 5000 };
        private bool _checking;

        private readonly Dictionary<string, RoleTile> _tiles = new Dictionary<string, RoleTile>(StringComparer.Ordinal);
        private List<IntPtr> _liveHandles = new List<IntPtr>();
        private int _hoverIndex = -1;
        private string _activeName = "";
        private readonly SearchBox _searchBox = new SearchBox();
        private readonly TextBox _search;
        private readonly List<Scheme> _schemes = new List<Scheme>();
        private readonly Dictionary<string, IntPtr> _thumbs = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        private int _firstSystem = -1;

        internal MainForm()
        {
            _libraryDir = DataFolder.Library;
            _backupFile = DataFolder.Backup;
            if (!Directory.Exists(_libraryDir)) Directory.CreateDirectory(_libraryDir);

            _search = _searchBox.Field;

            Text = "Cursor Selector";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Size = new Size(1240, 920);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.FontBody;
            BackColor = Theme.Ground;

            BuildLeftPanel();
            BuildRightPanel();
            BuildTiles();

            _confirm.Interval = 3000;
            _confirm.Tick += delegate
            {
                _confirm.Stop();
                _status.ForeColor = Theme.Dim;
                _status.Text = "Active:  " + _activeName;
            };

            _timer.Interval = 90;
            _timer.Tick += delegate
            {
                foreach (var tile in _tiles.Values) tile.Advance();
            };

            // Un folder sau un .zip lasat oriunde pe fereastra. Evenimentele de drop nu urca din
            // controalele copil, asa ca le legam pe fiecare.
            EnableDrop(this);

            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.F) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
            };

            // Actualizarea: la cateva secunde dupa pornire, apoi la fiecare ora cat sta deschisa.
            _updates.Tick += delegate { _updates.Interval = 60 * 60 * 1000; CheckForUpdates(false); };

            Shown += delegate { RefreshList(null); _timer.Start(); _updates.Start(); };
            FormClosed += delegate
            {
                _timer.Stop();
                foreach (var h in _liveHandles) Native.DestroyCursor(h);
                foreach (var h in _thumbs.Values) if (h != IntPtr.Zero) Native.DestroyCursor(h);
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

        /// <summary>Click dreapta pe o schema: arata fisierele ei, sau o scoate din biblioteca.</summary>
        private void BuildListMenu()
        {
            var menu = DarkMenu.Build();
            var show = new ToolStripMenuItem("Show the files");
            var remove = new ToolStripMenuItem("Remove from library\u2026");
            menu.Items.Add(show);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(remove);

            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                var scheme = _list.SelectedItem as Scheme;
                if (scheme == null) { e.Cancel = true; return; }
                show.Enabled = scheme.Location != null && Directory.Exists(scheme.Location);
                // Stergem doar ce am copiat noi; o schema inregistrata in Windows nu e a noastra.
                remove.Enabled = scheme.FromLibrary;
            };

            show.Click += delegate
            {
                var scheme = _list.SelectedItem as Scheme;
                if (scheme != null) System.Diagnostics.Process.Start("explorer.exe", scheme.Location);
            };

            remove.Click += delegate
            {
                var scheme = _list.SelectedItem as Scheme;
                if (scheme == null || !scheme.FromLibrary) return;
                if (MessageBox.Show(this,
                        "Delete the folder\n" + scheme.Location + "\n\nThe cursor files in it go to the Recycle Bin.",
                        "Remove " + scheme.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
                try
                {
                    // In Cos, nu definitiv: un pachet sters din greseala se poate pune la loc.
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(scheme.Location,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not remove it:\n" + ex.Message,
                        "Cursor Selector", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshList(null);
            };

            // Click dreapta muta si selectia, altfel meniul ar lucra pe alt rand decat cel aratat.
            _list.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                int index = _list.IndexFromPoint(e.Location);
                if (index >= 0 && index < _list.Items.Count) _list.SelectedIndex = index;
            };
            _list.ContextMenuStrip = menu;
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
            // Variabila, fiindca primul rand de sistem isi poarta deasupra propriul antet de sectiune.
            _list.DrawMode = DrawMode.OwnerDrawVariable;
            _list.MeasureItem += delegate(object sender, MeasureItemEventArgs e)
            {
                e.ItemHeight = e.Index == _firstSystem && _firstSystem > 0 ? 40 + SectionHead : 40;
            };
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
            BuildListMenu();

            var head = new Rail("Library") { Dock = DockStyle.Top, BackColor = Theme.Ground2 };
            var brand = new BrandHead { Dock = DockStyle.Top, Cursor = Cursors.Hand };
            new ToolTip().SetToolTip(brand, "Check for updates");
            brand.Click += delegate { CheckForUpdates(true); };

            // Cautare: 43 de scheme nu se mai parcurg cu ochiul.
            _searchBox.Dock = DockStyle.Top;
            _search.TextChanged += delegate { FillList(null); };
            _search.GotFocus += delegate { _searchBox.Invalidate(); };
            _search.LostFocus += delegate { _searchBox.Invalidate(); };
            _search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                // Sageata jos si Enter trec in lista, ca sa nu fie nevoie de mouse intre cautare si rezultat.
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter)
                {
                    _list.Focus();
                    e.Handled = e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape && _search.Text.Length > 0)
                {
                    _search.Clear();
                    e.Handled = e.SuppressKeyPress = true;
                }
            };

            _count.Dock = DockStyle.Top;
            _count.Height = 28;
            _count.Font = Theme.FontMono;          // cifrele in mono, altfel lista citeste a text rasfirat
            _count.ForeColor = Theme.Dim;
            _count.BackColor = Color.Transparent;
            _count.AutoSize = false;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 236,
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
            const string mail = "feedback@protagonistlabs.app", more = "More apps from Protagonist Labs", news = "What's new";
            var links = new LinkLabel
            {
                Text = "Feedback: " + mail + "\nWebsite \u00b7 " + news + "\n" + more,
                Size = new Size(312, 56),
                Margin = new Padding(0),
                Font = Theme.FontSmall,
                ForeColor = Theme.Dim,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline,
                TextAlign = ContentAlignment.TopCenter
            };
            links.Links.Clear();
            links.Links.Add(links.Text.IndexOf(mail), mail.Length, "mailto:" + mail + "?subject=Cursor%20Selector%20feedback");
            links.Links.Add(links.Text.IndexOf("Website"), 7, "https://protagonistlabs.app/cursorselector/?utm_source=cursorselector&utm_medium=app&utm_campaign=sidebar");
            links.Links.Add(links.Text.IndexOf(more), more.Length, "https://protagonistlabs.app/?utm_source=cursorselector&utm_medium=app&utm_campaign=more-apps");
            links.Links.Add(links.Text.IndexOf(news), news.Length, "whatsnew");
            LinkLabelLinkClickedEventHandler open = delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                if ((string)e.Link.LinkData == "whatsnew") { WhatsNewForm.Open(this); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute = true });
            };
            links.LinkClicked += open;
            buttons.Controls.AddRange(new Control[] { dropHint, import, openLib, refresh, links });

            // "Works with": nicio aplicatie sora nu adauga ceva aici, deci ramane doar randul cu
            // reducerea, si numai pentru cine a cumparat deja una. In culoarea textului din jur, ca restul.
            var offer = Ecosystem.Offer();
            if (offer.Count > 0)
            {
                const string lead = "Owners of one of our apps get 30% off the others: ";
                var others = new LinkLabel
                {
                    Text = lead + string.Join(" \u00b7 ", offer.Select(o => o.Key)),
                    AutoSize = false,
                    Size = new Size(312, 40),
                    Margin = new Padding(0, 6, 0, 0),
                    Font = Theme.FontSmall,
                    ForeColor = Theme.Dim,
                    LinkColor = Theme.Dim,
                    ActiveLinkColor = Theme.Text,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    TextAlign = ContentAlignment.TopCenter
                };
                others.Links.Clear();
                int at = lead.Length;
                foreach (var o in offer)
                {
                    others.Links.Add(at, o.Key.Length, o.Value);
                    at += o.Key.Length + 3;
                }
                others.LinkClicked += open;
                buttons.Controls.Add(others);
                buttons.Height += 46;
            }

            // Controlul cu Dock=Fill trebuie sa fie in fata, ca sa primeasca spatiul ramas.
            left.Controls.Add(_list);
            left.Controls.Add(_searchBox);
            left.Controls.Add(_count);
            left.Controls.Add(head);
            left.Controls.Add(brand);
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

        private const int SectionHead = 26;

        private void DrawSchemeRow(Graphics g, DrawItemEventArgs e)
        {
            var scheme = (Scheme)_list.Items[e.Index];
            // Nu folosim Graphics.Clear: umple toata suprafata controlului, nu doar randul curent.
            using (var background = new SolidBrush(Theme.Ground2))
                g.FillRectangle(background, e.Bounds);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = e.Bounds;
            if (e.Index == _firstSystem && _firstSystem > 0)
            {
                // Un singur antet acolo unde se termina biblioteca, in locul etichetei repetate
                // pe fiecare rand: 28 de pastile "LIBRARY" erau cel mai zgomotos lucru din bara.
                var head = new Rectangle(bounds.X + 12, bounds.Y + 8, bounds.Width - 24, SectionHead - 10);
                Theme.DrawTracked(g, "INSTALLED IN WINDOWS", Theme.FontBadge,
                    new Point(head.X, head.Y), Theme.Dimmer, 1.1f);
                int line = head.X + Theme.MeasureTracked(g, "INSTALLED IN WINDOWS", Theme.FontBadge, 1.1f) + 10;
                using (var pen = new Pen(Theme.Edge))
                    if (line < head.Right) g.DrawLine(pen, line, head.Y + 5, head.Right, head.Y + 5);
                bounds = new Rectangle(bounds.X, bounds.Y + SectionHead, bounds.Width, bounds.Height - SectionHead);
            }

            var r = new Rectangle(bounds.X, bounds.Y + 2, bounds.Width - 6, bounds.Height - 4);
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

            // Sageata schemei, desenata in rand: un nume spune mult mai putin decat cursorul insusi.
            var chip = new Rectangle(r.X + 10, r.Y + (r.Height - 26) / 2, 26, 26);
            IntPtr thumb = Thumb(scheme);
            Theme.FillRounded(g, chip, 8, thumb == IntPtr.Zero ? Theme.SwatchEmpty : Theme.Swatch);
            if (thumb != IntPtr.Zero)
            {
                g.SmoothingMode = SmoothingMode.Default;
                IntPtr hdc = g.GetHdc();
                Native.DrawIconEx(hdc, chip.X + 4, chip.Y + 4, thumb, 18, 18, 0, IntPtr.Zero, Native.DI_NORMAL);
                g.ReleaseHdc(hdc);
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }

            int right = r.Right - 12;
            if (scheme.Name == _activeName)
            {
                // Schema aplicata acum: pastila "ACTIVE", singura eticheta ramasa in lista.
                const float tracking = 0.9f;
                int width = Theme.MeasureTracked(g, "ACTIVE", Theme.FontBadge, tracking);
                int height = Theme.LineHeight(g, Theme.FontBadge);
                var badge = new Rectangle(right - width - 15, r.Y + (r.Height - height - 6) / 2, width + 15, height + 6);
                Theme.FillRounded(g, badge, badge.Height / 2, Theme.Accent);
                Theme.DrawTracked(g, "ACTIVE", Theme.FontBadge, new Point(badge.X + 7, badge.Y + 3), Theme.AccentInk, tracking);
                right = badge.X - 8;
            }

            var nameRect = new Rectangle(chip.Right + 12, r.Y, right - chip.Right - 12, r.Height);
            TextRenderer.DrawText(g, scheme.Name, Theme.FontBody, nameRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        /// <summary>
        /// Sageata unei scheme, incarcata o singura data si tinuta cat traieste fereastra: zeci de
        /// randuri ar reincarca acelasi fisier la fiecare redesenare a listei.
        /// </summary>
        private IntPtr Thumb(Scheme scheme)
        {
            IntPtr handle;
            if (_thumbs.TryGetValue(scheme.Name, out handle)) return handle;

            string path;
            handle = IntPtr.Zero;
            if (scheme.Map.TryGetValue("Arrow", out path) && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                handle = Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_CURSOR, 32, 32, Native.LR_LOADFROMFILE);
                if (handle == IntPtr.Zero) handle = Native.LoadCursorFromFile(path);
            }
            _thumbs[scheme.Name] = handle;
            return handle;
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

            _subtitle.Dock = DockStyle.Top;
            _subtitle.Height = 28;
            _subtitle.Font = Theme.FontMono;       // cifre si cai: mono
            _subtitle.ForeColor = Theme.Dim;
            _subtitle.BackColor = Color.Transparent;
            _subtitle.AutoSize = false;
            _subtitle.AutoEllipsis = true;

            var rail = new Rail("Cursor roles") { Dock = DockStyle.Top };

            _flow.Dock = DockStyle.Fill;
            _flow.AutoScroll = true;
            _flow.Padding = new Padding(14, 10, 14, 10);
            _flow.HandleCreated += delegate { Native.SetWindowTheme(_flow.Handle, "DarkMode_Explorer", null); };

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 68,
                BackColor = Color.Transparent
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
            // O coloana per grup, nu randuri cu antet: in randuri, cele 17 placi plus cele cinci
            // antete depasesc inaltimea cardului si ultimul grup ramanea taiat la mijloc.
            foreach (var group in Roles.Groups)
            {
                var column = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0, 0, 6, 0),
                    BackColor = Color.Transparent
                };

                column.Controls.Add(new Rail(group[0])
                {
                    Width = RoleTile.W,
                    Height = 22,
                    Margin = new Padding(3, 2, 3, 2)
                });

                for (int i = 1; i < group.Length; i++)
                {
                    var tile = new RoleTile(group[i], Roles.Display[group[i]]);
                    var captured = tile;
                    tile.Click += delegate { ShowCursorDetail(captured); };
                    column.Controls.Add(tile);
                    _tiles[group[i]] = tile;
                }
                _flow.Controls.Add(column);
            }
        }

        /// <summary>Cate cadre are cursorul: le cerem pe rand pana cand DrawIconEx refuza unul.</summary>
        private static int CountFrames(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return 0;
            using (var probe = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(probe))
            {
                IntPtr hdc = g.GetHdc();
                int frames = 0;
                while (frames < 200 && Native.DrawIconEx(hdc, -200, -200, handle, 32, 32, frames, IntPtr.Zero, Native.DI_NORMAL))
                    frames++;
                g.ReleaseHdc(hdc);
                return frames;
            }
        }

        private void ShowCursorDetail(RoleTile tile)
        {
            using (var detail = new CursorDetail(tile.Text, tile.File, tile.CursorHandle, CountFrames(tile.CursorHandle)))
                detail.ShowDialog(this);
        }

        private void ShowScheme(Scheme scheme)
        {
            UpdateApplyState();
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

                _tiles[role].Reset(handle, path);
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
            foreach (var handle in _thumbs.Values) if (handle != IntPtr.Zero) Native.DestroyCursor(handle);
            _thumbs.Clear();

            _schemes.Clear();
            _schemes.AddRange(Library.GetAll(_libraryDir));
            int library = _schemes.Count(s => s.FromLibrary);
            _count.Text = string.Format("{0} schemes  \u00b7  {1} library  \u00b7  {2} system",
                _schemes.Count, library, _schemes.Count - library);

            FillList(location);
            SetActive(CursorConfig.ActiveName());
            _restore.Enabled = File.Exists(_backupFile);
        }

        /// <summary>Umple lista cu schemele care trec de filtrul de cautare, pastrand selectia.</summary>
        private void FillList(string location)
        {
            var previous = _list.SelectedItem as Scheme;
            string previousName = previous == null ? null : previous.Name;
            string query = _search.Text.Trim();

            _list.BeginUpdate();
            _list.Items.Clear();
            _firstSystem = -1;
            foreach (var scheme in _schemes)
            {
                if (query.Length > 0 && scheme.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                if (!scheme.FromLibrary && _firstSystem < 0) _firstSystem = _list.Items.Count;
                _list.Items.Add(scheme);
            }
            _list.EndUpdate();
            Native.SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);

            if (_list.Items.Count == 0)
            {
                _flow.SetEmpty(
                    _schemes.Count == 0 ? "No schemes yet" : "Nothing matches that",
                    _schemes.Count == 0
                        ? "Drop a cursor folder or a .zip here, or use Import and apply."
                        : "Try part of a scheme's name, or clear the search.");
                ShowScheme(null);
                return;
            }
            _flow.SetEmpty(null, null);

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
            ShowScheme(_list.SelectedItem as Scheme);
        }

        private void SetActive(string name)
        {
            _activeName = name;
            _status.Text = "Active:  " + name;
            _list.Invalidate();
            UpdateApplyState();
        }

        /// <summary>
        /// Butonul principal nu mai cheama la o actiune care nu schimba nimic: cand schema aleasa
        /// e chiar cea pusa, spune asta si se stinge.
        /// </summary>
        private void UpdateApplyState()
        {
            var scheme = _list.SelectedItem as Scheme;
            bool applied = scheme != null && scheme.Name == _activeName;
            _apply.Text = applied ? "Already applied" : "Apply selected scheme";
            _apply.Enabled = scheme != null && !applied;
        }

        /// <summary>
        /// Confirmarea sta trei secunde in bara de jos, apoi randul revine la schema activa:
        /// pana aici, singurul semn ca s-a aplicat ceva era ca textul din colt se schimba.
        /// </summary>
        /// <summary>
        /// Pe un fir din pool: reteaua nu are voie sa blocheze fereastra. Verificarea automata tace cand nu
        /// e nimic nou sau cand n-a putut verifica (reincearca peste o ora); cea ceruta (click pe versiune)
        /// raspunde mereu, si spune altfel "nimic nou" decat "n-am putut verifica".
        /// </summary>
        private void CheckForUpdates(bool manual)
        {
            if (_checking) return;
            _checking = true;
            if (manual) Confirm("Checking for updates\u2026");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool failed;
                var waiting = Updater.UpdateInPlace(out failed);
                BeginInvoke((Action)delegate
                {
                    _checking = false;
                    if (waiting != null) Confirm("Updated to " + waiting + " \u2014 the next start opens it");
                    else if (!manual) return;
                    else if (failed) Confirm("Couldn't check for updates \u2014 no connection or GitHub is busy. Try again later.");
                    else Confirm("No newer version found (" + BrandHead.Version + ")");
                });
            });
        }

        private void Confirm(string message)
        {
            _status.ForeColor = Theme.Accent;
            _status.Text = message;
            _confirm.Stop();
            _confirm.Start();
        }

        private void OnApply(object sender, EventArgs e)
        {
            var scheme = _list.SelectedItem as Scheme;
            if (scheme == null) return;
            try
            {
                CursorConfig.Apply(scheme, _backupFile);
                SetActive(scheme.Name);
                Confirm("Applied to Windows");
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
                Confirm("Restored your saved cursors");
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

    /// <summary>
    /// Dosarul comun al aplicatiilor Protagonist Labs (%APPDATA%\ProtagonistLabs, contractul e in
    /// Ecosistem.md din vault). Aplicatia isi scrie cartea in apps\cursorselector.json la fiecare
    /// pornire si citeste cartile celorlalte doar pentru randul cu reducerea din bara. Datele ei raman
    /// portabile, langa exe; doar cartea sta in AppData. Nimic de acolo nu e de incredere: un fisier
    /// lipsa sau stricat e ca si cum n-ar exista, fara nicio eroare afisata.
    /// </summary>
    internal static class Ecosystem
    {
        internal static readonly string Apps = Path.Combine(Env.AppData, "ProtagonistLabs", "apps");

        private static readonly string[][] PaidApps =
        {
            new[] { "shelf", "Shelf" }, new[] { "stash", "Stash" }, new[] { "frostpane", "Frostpane" },
            new[] { "reel", "Reel" }, new[] { "planner", "Planner" }, new[] { "reminder", "Reminder" },
        };

        /// <summary>Pe un fir din pool: un disc plin sau un fisier blocat nu are voie sa intarzie pornirea.</summary>
        internal static void WriteCard()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var card = new Dictionary<string, object>
                    {
                        { "schema", 1 },
                        { "updated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                        { "id", "cursorselector" },
                        { "name", "Cursor Selector" },
                        { "version", BrandHead.Version },
                        { "exe", Application.ExecutablePath },
                        { "data", DataFolder.Root },
                        { "edition", "free" },
                        { "paid", false },
                        { "page", "https://protagonistlabs.app/cursorselector/" },
                        { "commands", new Dictionary<string, object>() },
                    };
                    Directory.CreateDirectory(Apps);
                    string path = Path.Combine(Apps, "cursorselector.json"), tmp = path + ".tmp";
                    File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(card), new System.Text.UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);
                }
                catch (Exception ex)
                {
                    // Cartea e o politete fata de celelalte aplicatii; fara ea Cursor Selector merge la fel.
                    if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                }
            });
        }

        /// <summary>Cartea unei aplicatii instalate; null daca lipseste, e stricata sau exe-ul nu mai exista.</summary>
        private static Dictionary<string, object> Read(string id)
        {
            try
            {
                var card = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(Path.Combine(Apps, id + ".json")));
                object schema, exe;
                if (card == null || !card.TryGetValue("schema", out schema) || !(schema is int) || (int)schema != 1
                    || !card.TryGetValue("exe", out exe) || !(exe is string) || !File.Exists((string)exe))
                    return null;
                return card;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                    || ex is InvalidOperationException) return null;
                throw;
            }
        }

        /// <summary>
        /// Aplicatiile platite neinstalate (cel mult 4), fiecare cu pagina si codul ei de reducere - dar
        /// numai cand utilizatorul a cumparat deja una (o carte cu paid si edition "full"). Altfel, nimic.
        /// </summary>
        internal static List<KeyValuePair<string, string>> Offer()
        {
            var offer = new List<KeyValuePair<string, string>>();
            bool bought = PaidApps.Any(app =>
            {
                var card = Read(app[0]);
                object paid, edition;
                return card != null && card.TryGetValue("paid", out paid) && true.Equals(paid)
                       && card.TryGetValue("edition", out edition) && "full".Equals(edition);
            });
            if (!bought) return offer;

            foreach (var app in PaidApps)
            {
                if (offer.Count == 4) break;
                if (Read(app[0]) != null) continue;
                offer.Add(new KeyValuePair<string, string>(app[1],
                    "https://protagonistlabs.app/" + app[0] + "/?code=" + (app[0] == "shelf" ? "SECOND30SHELF" : "SECOND30")
                    + "&utm_source=cursorselector&utm_medium=app&utm_campaign=works-with"));
            }
            return offer;
        }
    }

    /// <summary>
    /// Actualizarea pe loc, ca la HotkeyScan: Cursor Selector e un singur .exe portabil, asa ca cel mai nou
    /// CursorSelector.exe din release-ul GitHub se descarca langa cel care ruleaza, se verifica dupa marimea
    /// si SHA-256-ul publicate de GitHub, si ia locul vechiului. Windows lasa un .exe pornit sa fie redenumit,
    /// deci vechiul se da la o parte ca CursorSelector.exe.old (sters la pornirea urmatoare) si nimic nu se
    /// inchide: urmatoarea pornire e versiunea noua. Un dosar in care nu putem scrie e lasat in pace.
    /// </summary>
    internal static class Updater
    {
        private const string Repo = "limburatorul/cursor-selector", Asset = "CursorSelector.exe";
        private static Version _waiting;   // ce asteapta deja urmatoarea pornire; nu descarcam de doua ori

        /// <summary>
        /// Versiunea care asteapta urmatoarea pornire, sau null cand n-a fost nimic de facut. failed spune daca
        /// null inseamna "n-am putut verifica" (offline, 403/5xx, raspuns stricat, descarcare respinsa), nu
        /// "nu e nimic mai nou". Merge sub orice nume i-a dat utilizatorul (CursorSelector (1).exe).
        /// </summary>
        internal static Version UpdateInPlace(out bool failed)
        {
            failed = false;
            if (_waiting != null) return _waiting;
            failed = true;
            string exe = Application.ExecutablePath;
            try { File.Delete(exe + ".old"); } catch (IOException) { } catch (UnauthorizedAccessException) { }

            string fresh = exe + ".new";
            try
            {
                // Framework-ul vechi nu porneste singur cu TLS 1.2, iar GitHub nu primeste altceva.
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                Dictionary<string, object> root;
                using (var web = new System.Net.WebClient())
                {
                    web.Headers[System.Net.HttpRequestHeader.UserAgent] = "CursorSelector";
                    root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                        web.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest"));
                }
                Version latest, current = new Version(BrandHead.Version);
                if (!Version.TryParse(((root["tag_name"] as string) ?? "").TrimStart('v', 'V'), out latest)) return null;
                if (latest <= current) { failed = false; return null; }

                var asset = ((System.Collections.IEnumerable)root["assets"]).OfType<Dictionary<string, object>>()
                    .FirstOrDefault(a => Asset.Equals(a["name"]));
                if (asset == null) return null;
                object d;
                string digest = asset.TryGetValue("digest", out d) ? d as string : null;
                if (digest == null || !digest.StartsWith("sha256:")) return null;   // n-avem cu ce verifica: nu se ruleaza

                using (var web = new System.Net.WebClient())
                {
                    web.Headers[System.Net.HttpRequestHeader.UserAgent] = "CursorSelector";
                    web.DownloadFile((string)asset["browser_download_url"], fresh);
                }

                // Marimea prinde o descarcare taiata, checksum-ul restul; o copie diferita nu porneste niciodata.
                if (new FileInfo(fresh).Length != Convert.ToInt64(asset["size"])) { File.Delete(fresh); return null; }
                string hash;
                using (var sha = new System.Security.Cryptography.SHA256Managed())
                using (var file = File.OpenRead(fresh))
                    hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                if (!hash.Equals(digest.Substring(7), StringComparison.OrdinalIgnoreCase)) { File.Delete(fresh); return null; }

                if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
                File.Move(exe, exe + ".old");
                try { File.Move(fresh, exe); }
                catch { File.Move(exe + ".old", exe); throw; }   // dosarul nu ramane niciodata fara .exe
                failed = false;
                return _waiting = latest;
            }
            catch (Exception ex)
            {
                // Offline, limita GitHub, un raspuns ciudat sau un dosar fara drept de scriere: data viitoare.
                if (!(ex is System.Net.WebException || ex is IOException || ex is UnauthorizedAccessException
                      || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException
                      || ex is InvalidCastException || ex is FormatException)) throw;
                try { File.Delete(fresh); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return null;
            }
        }
    }

    /// <summary>
    /// Unde citeste si scrie aplicatia in afara folderului ei. Build-ul de test (MOVETEST, facut de mana,
    /// niciodata livrat) muta totul pe copii: AppData din variabila CS_TEST_APPDATA, registrul sub
    /// HKCU\Software\CursorSelectorTest, ca testele sa nu atinga cursoarele, datele sau cartea userului.
    /// </summary>
    internal static class Env
    {
#if MOVETEST
        internal static readonly string AppData = Environment.GetEnvironmentVariable("CS_TEST_APPDATA");
        internal const string Reg = @"Software\CursorSelectorTest\";
#else
        internal static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        internal const string Reg = "";
#endif
    }

    /// <summary>
    /// Din 1.4.0 tot ce tine Cursor Selector sta in %APPDATA%\ProtagonistLabs\Cursor Selector (regula studioului,
    /// nota din vault Ecosistem-v3-foldere): Library\ (schemele), backup.json (cursoarele dinainte de primul
    /// Apply) si move.log. Pana atunci stateau langa exe; exe-ul ramane portabil.
    ///
    /// Mutarea, la fiecare pornire pana s-a facut: fiecare schema din Library separat (un folder = o schema,
    /// deci si un zip "with-cursors" despachetat iar langa exe isi aduce schemele), apoi backup.json. Pe
    /// acelasi disc e o redenumire. Pe alt disc (exe-ul pe D:, AppData pe C:) e o copie intreaga intr-un
    /// folder temporar, redenumit abia la sfarsit, si numai dupa aceea se sterge originalul: niciodata o
    /// copie pe jumatate. Ce exista deja in locul nou nu se amesteca: vechiul ramane, cu un rand in move.log.
    /// Ce nu se poate muta acum ramane pe loc si se incearca la pornirea urmatoare.
    ///
    /// Schema activa (HKCU\Control Panel\Cursors) si lista din Proprietati mouse (...\Schemes) tin cai
    /// absolute spre fisierele din Library: Repoint le muta pe cele ale caror fisiere sunt acum in locul nou.
    /// </summary>
    internal static class DataFolder
    {
        internal static readonly string Root = Path.Combine(Env.AppData, "ProtagonistLabs", "Cursor Selector");
        internal static readonly string Library = Path.Combine(Root, "Library");
        internal static readonly string Backup = Path.Combine(Root, "backup.json");
        private static readonly string OldRoot = Path.GetDirectoryName(Application.ExecutablePath);
        private static readonly string OldLibrary = Path.Combine(OldRoot, "Library");
        internal static readonly string OldBackup = Path.Combine(OldRoot, "backup.json");

        internal static void Move()
        {
            // Un exe pus chiar in folderul de date nu are ce muta.
            if (string.Equals(Path.GetFullPath(OldRoot).TrimEnd('\\'), Path.GetFullPath(Root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            if (Directory.Exists(OldLibrary))
            {
                foreach (string entry in Directory.GetFileSystemEntries(OldLibrary))
                    MoveOne(entry, Path.Combine(Library, Path.GetFileName(entry)));
                try { Directory.Delete(OldLibrary); }                       // doar daca a ramas gol
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            MoveOne(OldBackup, Backup);
            RepointRegistry();
        }

        private static void MoveOne(string from, string to)
        {
            bool dir = Directory.Exists(from);
            if (!dir && !File.Exists(from)) return;
            if (Directory.Exists(to) || File.Exists(to)) { Log("kept " + from + ": " + to + " already exists"); return; }
            string staging = to + ".moving";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase))
                {
                    if (dir) Directory.Move(from, to); else File.Move(from, to);
                    Log("moved " + from + " to " + to);
                    return;
                }
                Delete(staging);
                if (dir) CopyTree(from, staging); else File.Copy(from, staging);
                if (dir) Directory.Move(staging, to); else File.Move(staging, to);
                Log("copied " + from + " to " + to + (Delete(from) ? ", original removed" : ", original could not be removed"));
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                Delete(staging);
                Log("not moved yet " + from + ": " + ex.Message);
            }
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (string d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        private static bool Delete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>O cale spre vechea Library al carei fisier e acum in cea noua; orice alta cale ramane cum e.</summary>
        internal static string Repoint(string path)
        {
            string old = OldLibrary + "\\";
            if (path == null || !path.StartsWith(old, StringComparison.OrdinalIgnoreCase)) return path;
            string moved = Path.Combine(Library, path.Substring(old.Length));
            return File.Exists(moved) && !File.Exists(path) ? moved : path;
        }

        private static void RepointRegistry()
        {
            bool active = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CursorConfig.CursorsPath, true))
                    if (key != null)
                        foreach (string name in key.GetValueNames())
                        {
                            string v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string, w = Repoint(v);
                            if (w != v) { key.SetValue(name, w, key.GetValueKind(name)); active = true; }
                        }
                using (var key = Registry.CurrentUser.OpenSubKey(CursorConfig.SchemesPath, true))
                    if (key != null)
                        foreach (string name in key.GetValueNames())
                        {
                            string v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                            if (v == null) continue;
                            string w = string.Join(",", v.Split(',').Select(Repoint).ToArray());
                            if (w != v) key.SetValue(name, w, key.GetValueKind(name));
                        }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)) throw;
            }
#if !MOVETEST
            // Windows reciteste schema activa, acum din locul nou.
            if (active) Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, Native.SPIF_UPDATEINIFILE_SENDCHANGE);
#endif
        }

        private static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.AppendAllText(Path.Combine(Root, "move.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + "\r\n");
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// What's new, ca la celelalte aplicatii ale studioului: versiunile in stanga, sub luna in care au iesit,
    /// cea aleasa in dreapta sub bannerul ei de 960 x 170. Notele sunt whatsnew.txt, inglobat in exe (build.cmd),
    /// deci merge fara internet si arata toate versiunile. Se deschide singura o data dupa un update (vazuta
    /// cand e inchisa de user, nu cand se deschide) si din linkul What's new din bara.
    /// </summary>
    internal sealed class WhatsNewForm : Form
    {
        private sealed class Release
        {
            internal string Version, Title;
            internal DateTime Date;
            internal readonly List<string> Lines = new List<string>();
        }

        private const string SeenKey = Env.Reg + @"Software\Cursor Selector";
        private static WhatsNewForm _open;
        private readonly List<Release> _releases = ReadNotes();
        private readonly List<Row> _rows = new List<Row>();
        private readonly Banner _banner = new Banner();
        private readonly FlowLayoutPanel _notes = new FlowLayoutPanel();
        private int _shown = -1;

        /// <summary>Doar dupa un update: o instalare noua n-are ce sa-i spuna.</summary>
        internal static void ShowOnce(Form owner, bool ranBefore)
        {
            string seen;
            using (var k = Registry.CurrentUser.OpenSubKey(SeenKey)) seen = k == null ? null : k.GetValue("WhatsNewSeen") as string;
            if (seen == BrandHead.Version) return;
            if (seen != null || ranBefore) Open(owner);
            else MarkSeen();
        }

        internal static void Open(Form owner)
        {
            if (_open != null) { _open.Activate(); return; }
            _open = new WhatsNewForm();
            _open.FormClosed += delegate(object s, FormClosedEventArgs e)
            {
                _open = null;
                // Inchisa odata cu aplicatia (sau cu Windows-ul) nu conteaza ca vazuta: revine la pornirea urmatoare.
                if (e.CloseReason == CloseReason.UserClosing) MarkSeen();
            };
            _open.Show(owner);
        }

        private static void MarkSeen()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(SeenKey)) k.SetValue("WhatsNewSeen", BrandHead.Version);
        }

        private static List<Release> ReadNotes()
        {
            var list = new List<Release>();
            using (var stream = typeof(WhatsNewForm).Assembly.GetManifestResourceStream("whatsnew.txt"))
            using (var reader = new StreamReader(stream))
                foreach (string raw in reader.ReadToEnd().Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    var head = Regex.Match(line, @"^## (\S+) \| (\d{4}-\d\d-\d\d) \| (.+)$");
                    if (head.Success)
                        list.Add(new Release { Version = head.Groups[1].Value, Date = DateTime.ParseExact(head.Groups[2].Value, "yyyy-MM-dd", null), Title = head.Groups[3].Value });
                    else if (list.Count > 0 && line.StartsWith("- "))
                        list[list.Count - 1].Lines.Add(line.Substring(2).Replace("**", "").Replace("`", ""));
                }
            return list;
        }

        private WhatsNewForm()
        {
            Text = "What's new — Cursor Selector";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Size = new Size(1240, 720);            // loc pentru lista si un banner de 960 x 170
            MinimumSize = new Size(700, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Ground2;
            Font = Theme.FontBody;
            ShowInTaskbar = false;
            KeyPreview = true;

            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Left, Width = 232, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(18, 16, 0, 12), BackColor = Theme.Ground2
            };
            string month = null;
            for (int i = 0; i < _releases.Count; i++)
            {
                var r = _releases[i];
                string m = r.Date.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                if (m != month)
                {
                    month = m;
                    nav.Controls.Add(new Label
                    {
                        Text = m, AutoSize = false, Size = new Size(196, 26), Font = Theme.FontSmall, ForeColor = Theme.Dim,
                        TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(0, i == 0 ? 0 : 10, 0, 2)
                    });
                }
                var row = new Row(r.Version, i == 0 ? "Latest" : r.Date.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)) { AccessibleName = r.Version + ", " + r.Title };
                int index = i;
                row.Click += delegate { ShowRelease(index); };
                new ToolTip().SetToolTip(row, r.Title);
                _rows.Add(row);
                nav.Controls.Add(row);
            }

            _banner.Dock = DockStyle.Top;
            _notes.Dock = DockStyle.Fill;
            _notes.FlowDirection = FlowDirection.TopDown;
            _notes.WrapContents = false;
            _notes.AutoScroll = true;
            _notes.Padding = new Padding(0, 14, 0, 0);
            _notes.Resize += delegate { foreach (Control c in _notes.Controls) c.MaximumSize = new Size(NoteWidth, 0); };
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 18, 0), BackColor = Theme.Ground2 };
            right.Controls.Add(_notes);
            right.Controls.Add(_banner);
            _notes.BringToFront();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Ground2 };
            var close = new FlatButton("Close", true) { Size = new Size(140, 50), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            close.Location = new Point(bottom.Width - close.Width - 14, 4);
            close.Click += delegate { Close(); };
            bottom.Controls.Add(close);

            Controls.Add(bottom);
            Controls.Add(nav);
            Controls.Add(right);
            right.BringToFront();

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
                {
                    ShowRelease(Math.Max(0, Math.Min(_releases.Count - 1, _shown + (e.KeyCode == Keys.Down ? 1 : -1))));
                    _rows[_shown].Focus();
                    e.Handled = e.SuppressKeyPress = true;
                }
            };
            int current = _releases.FindIndex(r => r.Version == BrandHead.Version);
            ShowRelease(Math.Max(0, current));
            Shown += delegate { _rows[_shown].Focus(); };
        }

        private int NoteWidth { get { return Math.Max(200, Math.Min(960, _notes.ClientSize.Width - 24)); } }

        private void ShowRelease(int i)
        {
            if (i == _shown || i < 0 || i >= _releases.Count) return;
            _shown = i;
            var r = _releases[i];
            for (int j = 0; j < _rows.Count; j++) _rows[j].Selected = j == i;
            _banner.SetPicture(Picture(r));
            _notes.SuspendLayout();
            _notes.Controls.Clear();
            _notes.Controls.Add(new Label
            {
                Text = r.Date.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                AutoSize = true, ForeColor = Theme.Dim, Margin = new Padding(0, 0, 0, 8)
            });
            foreach (string line in r.Lines)
                _notes.Controls.Add(new Label
                {
                    Text = "•  " + line, AutoSize = true, MaximumSize = new Size(NoteWidth, 0),
                    ForeColor = Theme.Text, Margin = new Padding(0, 3, 0, 5)
                });
            _notes.ResumeLayout();
            _notes.AutoScrollPosition = Point.Empty;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            if (Native.DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
        }

        /// <summary>Poza versiunii (WhatsNew\x.y.jpg, inglobata), altfel cea a lunii in care a iesit (WhatsNew\mMM.jpg).</summary>
        private static Bitmap Picture(Release r)
        {
            var exe = typeof(WhatsNewForm).Assembly;
            string minor = string.Join(".", r.Version.Split('.').Take(2));
            using (var stream = exe.GetManifestResourceStream("WhatsNew." + minor + ".jpg")
                             ?? exe.GetManifestResourceStream("WhatsNew.m" + r.Date.ToString("MM") + ".jpg"))
            using (var image = Image.FromStream(stream))
                return new Bitmap(image);   // o copie: Image.FromStream vrea fluxul deschis cat traieste imaginea
        }

        /// <summary>Un rand din lista: versiunea la stanga, data (sau Latest) la dreapta.</summary>
        private sealed class Row : Control
        {
            private readonly string _date;
            private bool _hover, _selected;

            internal Row(string version, string date)
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                       | ControlStyles.Selectable, true);
                Text = version;
                _date = date;
                Size = new Size(196, 34);
                Margin = new Padding(0, 1, 0, 1);
                BackColor = Theme.Ground2;
                Cursor = Cursors.Hand;
                TabStop = true;
                AccessibleRole = AccessibleRole.ListItem;
            }

            internal bool Selected { set { _selected = value; Invalidate(); } }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
            protected override void OnKeyUp(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
                base.OnKeyUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Ground2);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var box = new Rectangle(0, 0, Width - 1, Height - 1);
                if (_selected || _hover) Theme.FillRounded(g, box, 8, _selected ? Theme.SelectedRow : Theme.Panel2);
                if (Focused) Theme.DrawRounded(g, box, 8, Theme.Accent);
                var inner = Rectangle.Inflate(box, -10, 0);
                TextRenderer.DrawText(g, Text, Theme.FontBody, inner, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, _date, Theme.FontSmall, inner, Theme.Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        /// <summary>960 x 170, mai mic odata cu fereastra: poza versiunii (sau a lunii ei), cu colturile rotunjite.</summary>
        private sealed class Banner : Control
        {
            private Bitmap _picture;

            internal Banner()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                       | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Ground2;
                Height = 170;
            }

            internal void SetPicture(Bitmap picture)
            {
                if (_picture != null) _picture.Dispose();
                _picture = picture;
                Invalidate();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                int h = Math.Min(Width, 960) * 170 / 960;
                if (h > 0 && h != Height) Height = h;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Ground2);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Min(Width, 960) / 960f;
                g.ScaleTransform(scale, scale);
                var all = new Rectangle(0, 0, 960, 170);
                using (var clip = Theme.RoundedPath(all, 10)) g.SetClip(clip);
                g.DrawImage(_picture, all);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _picture != null) _picture.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Native.TryEnableDarkControls();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Inainte de 1.4.0 nu se nota nimic: o carte sau un backup deja existente inseamna un update.
            bool ranBefore = File.Exists(Path.Combine(Ecosystem.Apps, "cursorselector.json"))
                || File.Exists(DataFolder.OldBackup) || File.Exists(DataFolder.Backup);
            DataFolder.Move();   // inainte ca fereastra sa citeasca biblioteca
            Ecosystem.WriteCard();
            var form = new MainForm();
            form.Shown += delegate { WhatsNewForm.ShowOnce(form, ranBefore); };
            Application.Run(form);
        }
    }
}
