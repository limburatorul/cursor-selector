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
[assembly: System.Reflection.AssemblyVersion("1.5.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.5.0")]

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
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, [MarshalAs(UnmanagedType.Bool)] out bool pvParam, uint fWinIni);
        internal const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

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
    /// Windows 11 in dark mode, the way Settings and PowerToys look: the same greys, the accent colour the
    /// user picked in Windows, Segoe UI Variable and small corners. Until 1.4 the app had a look of its own
    /// (glow, aura, spaced-out capitals in a mono font) that read as generated rather than native. The
    /// colours are opaque: WinForms paints opaque controls and has no layer to blend WinUI's white-with-alpha
    /// fills onto, so Over() composes them against the colour underneath.
    /// </summary>
    internal static class Theme
    {
        internal static readonly Color Ground2 = Color.FromArgb(0x20, 0x20, 0x20);   // the window: list pane, title bar
        internal static readonly Color Ground = Color.FromArgb(0x27, 0x27, 0x27);    // the content layer on the right

        internal static readonly Color Text = Color.White;
        internal static readonly Color Dim = Color.FromArgb(0xCF, 0xCF, 0xCF);       // secondary text
        internal static readonly Color Dimmer = Color.FromArgb(0x99, 0x99, 0x99);    // tertiary text
        internal static readonly Color Disabled = Color.FromArgb(0x71, 0x71, 0x71);

        internal static readonly Color Panel = Color.FromArgb(0x2B, 0x2B, 0x2B);      // card
        internal static readonly Color Panel2 = Color.FromArgb(0x32, 0x32, 0x32);     // card under the pointer
        internal static readonly Color Edge = Color.FromArgb(0x1C, 0x1C, 0x1C);       // card and layer border
        internal static readonly Color EdgeBright = Color.FromArgb(0x3A, 0x3A, 0x3A); // control border
        internal static readonly Color FieldLine = Color.FromArgb(0x9A, 0x9A, 0x9A);  // a text field's bottom edge
        internal static readonly Color PanelOnSide = Color.FromArgb(0x2D, 0x2D, 0x2D); // list row under the pointer
        internal static readonly Color SelectedRow = Color.FromArgb(0x2D, 0x2D, 0x2D);
        internal static readonly Color MenuFill = Color.FromArgb(0x2C, 0x2C, 0x2C);
        internal static readonly Color Swatch = Color.FromArgb(0xD6, 0xD6, 0xD6);     // behind a cursor too dark for the cards

        // AccentPalette holds the user's accent in eight shades (RGBA each, lightest first). Dark-mode apps
        // fill with the second one and write links in the first.
        private static readonly byte[] Palette = ReadPalette();
        internal static readonly Color Accent = Shade(1, Color.FromArgb(0x60, 0xCD, 0xFF));
        internal static readonly Color AccentText = Shade(0, Color.FromArgb(0x99, 0xEB, 0xFF));
        internal static readonly Color AccentHover = Mix(Accent, Ground, 0.1);
        internal static readonly Color AccentPressed = Mix(Accent, Ground, 0.2);
        internal static readonly Color AccentInk = Color.Black;

        internal const int Radius = 4;          // controls and cards
        internal const int RadiusLarge = 8;     // layers, menus, dialogs

        private static byte[] ReadPalette()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                return key == null ? null : key.GetValue("AccentPalette") as byte[];
        }

        private static Color Shade(int index, Color fallback)
        {
            if (Palette == null || Palette.Length < index * 4 + 3) return fallback;
            return Color.FromArgb(Palette[index * 4], Palette[index * 4 + 1], Palette[index * 4 + 2]);
        }

        internal static Color Mix(Color under, Color over, double alpha)
        {
            return Color.FromArgb(
                (int)Math.Round(under.R + (over.R - under.R) * alpha),
                (int)Math.Round(under.G + (over.G - under.G) * alpha),
                (int)Math.Round(under.B + (over.B - under.B) * alpha));
        }

        /// <summary>WinUI's fills are white at a few percent; this is that white laid over <paramref name="ground"/>.</summary>
        internal static Color Over(Color ground, double alpha) { return Mix(ground, Color.White, alpha); }

        /// <summary>Windows' "Animation effects" switch; every fade here stops when it is off.</summary>
        internal static bool AnimationsOn
        {
            get
            {
                bool on;
                return Native.SystemParametersInfo(Native.SPI_GETCLIENTAREAANIMATION, 0, out on, 0) && on;
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

        private static Font Pick(float size, params string[] families)
        {
            foreach (var name in families)
                if (Installed.Contains(name)) return new Font(name, size);
            return new Font(FontFamily.GenericSansSerif, size);
        }

        // WinUI's type ramp: body 14px, caption 12px, subtitle 20px, title 28px. Segoe UI Variable comes with
        // Windows 11, Windows 10 has Segoe UI. GDI lists each weight as a family of its own and cuts names at
        // 31 characters, hence "Display Semib".
        internal static readonly Font FontBody = Pick(10.5F, "Segoe UI Variable Text", "Segoe UI");
        internal static readonly Font FontBodyStrong = Pick(10.5F, "Segoe UI Variable Text Semibold", "Segoe UI Semibold");
        internal static readonly Font FontSmall = Pick(9F, "Segoe UI Variable Small", "Segoe UI");
        internal static readonly Font FontSubtitle = Pick(15F, "Segoe UI Variable Display Semib", "Segoe UI Semibold");
        internal static readonly Font FontDisplay = Pick(21F, "Segoe UI Variable Display Semib", "Segoe UI Semibold");
        internal static readonly Font FontIcon = Pick(12F, "Segoe Fluent Icons", "Segoe MDL2 Assets");

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

        /// <summary>The keyboard focus mark of WinUI: a 2px white ring just inside the control.</summary>
        internal static void DrawFocus(Graphics g, Rectangle r, int radius)
        {
            using (var path = RoundedPath(Rectangle.Inflate(r, -1, -1), radius))
            using (var pen = new Pen(Color.White, 2f))
                g.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// A cursor frame as an ARGB bitmap. GDI draws a cursor into a device context and drops the alpha of a
    /// GDI+ bitmap, so the frame is drawn once on black and once on white: where the two agree the pixel is
    /// opaque, and the difference gives the alpha of soft edges and shadows.
    /// </summary>
    internal static class CursorArt
    {
        internal static Bitmap Render(IntPtr handle, int size)
        {
            int[] black = Draw(handle, size, Color.Black), white = Draw(handle, size, Color.White);
            var argb = new int[size * size];
            for (int i = 0; i < argb.Length; i++)
            {
                int b = black[i], w = white[i];
                int br = (b >> 16) & 255, bg = (b >> 8) & 255, bb = b & 255;
                int alpha = 255 - ((((w >> 16) & 255) - br) + (((w >> 8) & 255) - bg) + ((w & 255) - bb)) / 3;
                if (alpha <= 0) continue;
                alpha = Math.Min(255, alpha);
                argb[i] = alpha << 24 | Math.Min(255, br * 255 / alpha) << 16 | Math.Min(255, bg * 255 / alpha) << 8 | Math.Min(255, bb * 255 / alpha);
            }
            var art = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = art.LockBits(new Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.WriteOnly, art.PixelFormat);
            for (int y = 0; y < size; y++) Marshal.Copy(argb, y * size, data.Scan0 + y * data.Stride, size);
            art.UnlockBits(data);
            return art;
        }

        private static int[] Draw(IntPtr handle, int size, Color ground)
        {
            using (var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(ground);
                    IntPtr hdc = g.GetHdc();
                    Native.DrawIconEx(hdc, 0, 0, handle, size, size, 0, IntPtr.Zero, Native.DI_NORMAL);
                    g.ReleaseHdc(hdc);
                }
                return Pixels(bitmap);
            }
        }

        private static int[] Pixels(Bitmap bitmap)
        {
            int w = bitmap.Width, h = bitmap.Height;
            var pixels = new int[w * h];
            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * w, w);
            bitmap.UnlockBits(data);
            return pixels;
        }

        /// <summary>
        /// Almost no light pixels: on a dark card the cursor would all but vanish, so it gets a light square
        /// behind it. Most packs outline their cursors and never need one.
        /// </summary>
        /// <summary>
        /// How many pictures an animated cursor holds: the cFrames field of the "anih" chunk in a RIFF/ACON
        /// file, which is what DrawIconEx's step counts (cSteps is the play order and can be longer). DrawIconEx
        /// cannot tell by itself: it accepts any step for a still cursor, so asking it frame by frame found
        /// 200 frames in every .cur and kept still cards repainting.
        /// </summary>
        internal static int Frames(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return 0;
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
            if (data.Length < 12 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "RIFF"
                || System.Text.Encoding.ASCII.GetString(data, 8, 4) != "ACON") return 1;
            for (int at = 12; at + 8 <= data.Length; )
            {
                string id = System.Text.Encoding.ASCII.GetString(data, at, 4);
                int size = BitConverter.ToInt32(data, at + 4);
                if (id == "anih" && at + 8 + 12 <= data.Length) return Math.Max(1, BitConverter.ToInt32(data, at + 8 + 4));
                if (id == "LIST") { at += 12; continue; }        // a list's children follow its type tag
                if (size < 0) break;
                at += 8 + size + (size & 1);                       // chunks are padded to an even size
            }
            return 1;
        }

        internal static bool IsDark(Bitmap art)
        {
            int solid = 0, light = 0;
            foreach (int c in Pixels(art))
            {
                if (((c >> 24) & 255) < 200) continue;
                solid++;
                // Luma: how light the pixel looks, not its average channel.
                if ((((c >> 16) & 255) * 299 + ((c >> 8) & 255) * 587 + (c & 255) * 114) / 1000 > 128) light++;
            }
            return solid > 0 && light * 10 < solid;
        }

        /// <summary>
        /// Where the drawing is. A .cur keeps its picture in the corner by the hotspot (top left for an
        /// arrow, the middle for a crosshair), so drawn as is, cursors sit at different heights in their box.
        /// </summary>
        internal static Rectangle Bounds(Bitmap art)
        {
            int[] pixels = Pixels(art);
            int w = art.Width, left = w, top = art.Height, right = -1, bottom = -1;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (((pixels[i] >> 24) & 255) <= 16) continue;
                int x = i % w, y = i / w;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
            return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        /// <summary>How far to move a cursor drawn at <paramref name="size"/> so its drawing sits in the middle.</summary>
        internal static Point Centring(Bitmap art)
        {
            var b = Bounds(art);
            return b.IsEmpty ? Point.Empty : new Point((art.Width - b.Width) / 2 - b.X, (art.Height - b.Height) / 2 - b.Y);
        }
    }

    /// <summary>
    /// A value easing towards a target, at the pace of Windows' own controls (WinUI fades hover in about
    /// 100 ms). One shared 15 ms timer runs only while something is moving; with Windows' animation effects
    /// off, the value jumps straight to its target.
    /// </summary>
    internal sealed class Fade
    {
        private static readonly Timer Clock = new Timer { Interval = 15 };
        private static readonly List<Fade> Moving = new List<Fade>();

        private readonly int _ms;
        private readonly Action _changed;
        private readonly System.Diagnostics.Stopwatch _since = new System.Diagnostics.Stopwatch();
        private float _from, _to;
        internal float Value;

        static Fade()
        {
            Clock.Tick += delegate
            {
                foreach (var fade in Moving.ToArray()) fade.Step();
                if (Moving.Count == 0) Clock.Stop();
            };
        }

        internal Fade(int ms, Action changed)
        {
            _ms = ms;
            _changed = changed;
        }

        internal void Go(float target) { Run(Value, target); }

        internal void Run(float from, float target)
        {
            if (!Theme.AnimationsOn)
            {
                Moving.Remove(this);
                Value = target;
                _changed();
                return;
            }
            _from = from;
            _to = target;
            Value = from;
            _since.Restart();
            if (!Moving.Contains(this)) Moving.Add(this);
            Clock.Start();
            _changed();
        }

        private void Step()
        {
            double t = Math.Min(1, _since.Elapsed.TotalMilliseconds / _ms);
            Value = _from + (_to - _from) * (float)(1 - Math.Pow(1 - t, 3));   // ease-out cubic
            if (t >= 1) Moving.Remove(this);
            _changed();
        }
    }

    /// <summary>
    /// One cursor role as a card in the Settings style: the cursor on the left, its name beside it. The card
    /// draws everything itself, so a scheme change repaints 17 controls rather than three times as many.
    /// While the pointer is over a card it becomes that cursor, so a scheme can be tried before it is applied.
    /// </summary>
    internal sealed class RoleTile : Control
    {
        internal const int W = 208, H = 64, Box = 40;

        internal readonly string Role;
        internal string File;
        internal IntPtr CursorHandle = IntPtr.Zero;
        internal float Reveal = 1;      // the fade-in after a scheme change, driven by MainForm
        private bool _dark;
        private Point _centring;
        private int _step, _frames = 1;
        private readonly Fade _hover;

        internal RoleTile(string role, string display)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Role = role;
            Text = display;
            AccessibleName = display;
            AccessibleRole = AccessibleRole.PushButton;
            Size = new Size(W, H);
            Margin = new Padding(0, 0, 4, 4);
            BackColor = Theme.Ground;
            _hover = new Fade(110, Invalidate);
        }

        /// <param name="live">The same cursor at pointer size, worn by the pointer over the card.</param>
        internal void Reset(IntPtr handle, IntPtr live, string file)
        {
            CursorHandle = handle;
            File = file;
            _step = 0;
            _frames = Math.Max(1, CursorArt.Frames(file));
            _dark = false;
            _centring = Point.Empty;
            if (handle != IntPtr.Zero)
                using (var art = CursorArt.Render(handle, Box))
                {
                    _dark = CursorArt.IsDark(art);
                    _centring = CursorArt.Centring(art);
                }
            Cursor = live != IntPtr.Zero ? new Cursor(live) : Cursors.Default;
            Invalidate();
        }

        internal void Advance()
        {
            if (_frames > 1 && CursorHandle != IntPtr.Zero) { _step = (_step + 1) % _frames; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover.Go(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover.Go(0); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Theme.Mix(Theme.Panel, Theme.Panel2, _hover.Value);
            Theme.FillRounded(g, card, Theme.Radius, fill);
            Theme.DrawRounded(g, card, Theme.Radius, Theme.Edge);

            bool empty = CursorHandle == IntPtr.Zero;
            var box = new Rectangle(14, (Height - Box) / 2, Box, Box);
            if (!empty && _dark) Theme.FillRounded(g, Rectangle.Inflate(box, 3, 3), Theme.Radius, Theme.Swatch);
            g.SmoothingMode = SmoothingMode.Default;

            var label = new Rectangle(box.Right + 14, 0, Width - box.Right - 24, Height);
            TextRenderer.DrawText(g, empty ? Text + "\nWindows default" : Text, Theme.FontBody, label,
                empty ? Theme.Dimmer : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

            if (!empty)
            {
                IntPtr hdc = g.GetHdc();
                Native.DrawIconEx(hdc, box.X + _centring.X, box.Y + _centring.Y, CursorHandle, Box, Box, _step, IntPtr.Zero, Native.DI_NORMAL);
                g.ReleaseHdc(hdc);
            }

            if (Reveal < 1)
                using (var veil = new SolidBrush(Color.FromArgb((int)((1 - Reveal) * 255), fill)))
                    g.FillRectangle(veil, Rectangle.Inflate(card, -1, -1));
        }
    }

    /// <summary>
    /// A WinUI button: standard, accent (Primary), subtle (no fill until the pointer is on it, for the
    /// icon buttons) or the red "Buy me a pizza" one shared by the studio's free apps.
    /// </summary>
    internal sealed class FlatButton : Control
    {
        internal bool Primary;
        internal bool Subtle;
        internal string Glyph;          // a Segoe Fluent Icons character, for an icon-only button
        // Glow e culoarea butonului pizza in repaus; MainForm.Nudge o lumineaza cateva clipe.
        internal bool Pizza;
        internal Color Glow = PizzaRed;
        internal static readonly Color PizzaRed = Color.FromArgb(0xD6, 0x3A, 0x3A);
        internal bool Hovered { get { return _hover; } }
        private bool _hover;
        private bool _pressed;
        private readonly Fade _fade;

        internal FlatButton(string text, bool primary)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Text = text;
            Primary = primary;
            Font = Theme.FontBody;
            BackColor = Theme.Ground;
            _fade = new Fade(90, Invalidate);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; _fade.Go(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; _fade.Go(0); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
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
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            float t = _fade.Value;

            Color fill, ink;
            bool edge = false;
            if (Pizza)
            {
                fill = _pressed ? Color.FromArgb(0xBF, 0x30, 0x30) : Theme.Mix(Glow, Color.FromArgb(0xE5, 0x48, 0x48), t);
                ink = Color.White;
            }
            else if (Primary)
            {
                fill = !Enabled ? Theme.Over(BackColor, 0.158) : _pressed ? Theme.AccentPressed : Theme.Mix(Theme.Accent, Theme.AccentHover, t);
                ink = Enabled ? Theme.AccentInk : Theme.Dim;
            }
            else if (Subtle)
            {
                fill = _pressed ? Theme.Over(BackColor, 0.0326) : Theme.Over(BackColor, 0.0605 * t);
                ink = Enabled ? Theme.Text : Theme.Disabled;
            }
            else
            {
                fill = !Enabled ? Theme.Over(BackColor, 0.042) : _pressed ? Theme.Over(BackColor, 0.0326) : Theme.Over(BackColor, 0.0605 + 0.0232 * t);
                ink = Enabled ? (_pressed ? Theme.Dim : Theme.Text) : Theme.Disabled;
                edge = true;
            }

            if (fill != BackColor) Theme.FillRounded(g, r, Theme.Radius, fill);
            if (edge) Theme.DrawRounded(g, r, Theme.Radius, Theme.EdgeBright);

            const TextFormatFlags centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (string.IsNullOrEmpty(Glyph)) TextRenderer.DrawText(g, Text, Font, r, ink, centre);
            else TextRenderer.DrawText(g, Glyph, Theme.FontIcon, r, ink, centre);

            if (Focused && ShowFocusCues && Enabled) Theme.DrawFocus(g, r, Theme.Radius);
        }
    }

    /// <summary>The roles area: the cards grouped under headings; with nothing to show, the drop zone.</summary>
    internal sealed class CardPanel : FlowLayoutPanel
    {
        /// <summary>Ce scrie in zona cand nu e nimic de aratat; null cat timp sunt placi.</summary>
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
            if (EmptyMessage == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Chenar punctat: semnul obisnuit pentru "lasa ceva aici".
            var drop = new Rectangle(0, 8, Width - 1, Height - 24);
            using (var pen = new Pen(Theme.EdgeBright, 1.4f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 5f, 4f } })
            using (var path = Theme.RoundedPath(drop, Theme.RadiusLarge))
                g.DrawPath(pen, path);

            var middle = new Rectangle(drop.X, drop.Y + drop.Height / 2 - 34, drop.Width, 30);
            TextRenderer.DrawText(g, EmptyMessage, Theme.FontSubtitle, middle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
            TextRenderer.DrawText(g, EmptyHint, Theme.FontBody,
                new Rectangle(drop.X, middle.Bottom + 8, drop.Width, 26), Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }
    }

    /// <summary>
    /// Menus in the WinUI style: the flyout grey, rows 32px tall with a rounded highlight, and the window
    /// rounded by Windows 11 itself (a menu window is square, so a drawn corner would leave the old one showing).
    /// </summary>
    internal sealed class DarkMenu : ToolStripRenderer
    {
        internal static ContextMenuStrip Build()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenu(),
                BackColor = Theme.MenuFill,
                ForeColor = Theme.Text,
                Font = Theme.FontBody,
                ShowImageMargin = false,
                Padding = new Padding(4)
            };
            menu.ItemAdded += delegate(object s, ToolStripItemEventArgs e)
            {
                if (!(e.Item is ToolStripSeparator)) e.Item.Padding = new Padding(4, 6, 28, 6);
            };
            menu.HandleCreated += delegate
            {
                int round = 3;   // DWMWCP_ROUNDSMALL
                Native.DwmSetWindowAttribute(menu.Handle, 33, ref round, sizeof(int));
            };
            return menu;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.MenuFill);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.EdgeBright))
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRounded(e.Graphics, new Rectangle(0, 1, e.Item.Width - 1, e.Item.Height - 3), Theme.Radius,
                Theme.Over(Theme.MenuFill, 0.0605));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.EdgeBright))
                e.Graphics.DrawLine(pen, -4, e.Item.Height / 2, e.Item.Width + 4, e.Item.Height / 2);
        }
    }

    /// <summary>
    /// The search field as a WinUI TextBox: a borderless native TextBox inside a panel that draws the box,
    /// the grey bottom edge (accent and thicker while typing) and the magnifier.
    /// </summary>
    internal sealed class SearchBox : Panel
    {
        internal readonly TextBox Field = new TextBox();
        private static readonly Color Rest = Theme.Over(Theme.Ground2, 0.0605);
        private static readonly Color Typing = Color.FromArgb(0x1F, 0x1F, 0x1F);

        internal SearchBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 32;
            BackColor = Theme.Ground2;
            Cursor = Cursors.IBeam;

            Field.BorderStyle = BorderStyle.None;
            Field.BackColor = Rest;
            Field.ForeColor = Theme.Text;
            Field.Font = Theme.FontBody;
            Field.AccessibleName = "Search schemes";
            Field.HandleCreated += delegate
            {
                Native.SendMessage(Field.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, "Search schemes");
                Native.SetWindowTheme(Field.Handle, "DarkMode_CFD", null);
            };
            Field.GotFocus += delegate { Field.BackColor = Typing; Invalidate(); };
            Field.LostFocus += delegate { Field.BackColor = Rest; Invalidate(); };
            Controls.Add(Field);
            Click += delegate { Field.Focus(); };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int height = Field.PreferredHeight;
            Field.SetBounds(11, (Height - height) / 2, Math.Max(10, Width - 11 - 36), height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Ground2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool typing = Field.Focused;
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(g, box, Theme.Radius, typing ? Typing : Rest);
            Theme.DrawRounded(g, box, Theme.Radius, Theme.EdgeBright);

            using (var path = Theme.RoundedPath(box, Theme.Radius))
            using (var line = new SolidBrush(typing ? Theme.Accent : Theme.FieldLine))
            {
                g.SetClip(path);
                int thick = typing ? 2 : 1;
                g.FillRectangle(line, 0, Height - thick, Width, thick);
                g.ResetClip();
            }

            TextRenderer.DrawText(g, "", Theme.FontIcon, new Rectangle(Width - 34, 0, 26, Height), Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
    /// The content layer of a WinUI window: a lighter grey sheet beside the list pane, its top-left corner
    /// rounded. WS_EX_COMPOSITED: Windows compune tot arborele de copii intr-un buffer inainte de afisare,
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
            var g = e.Graphics;
            g.Clear(Theme.Ground2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Larger than the panel, so only the top-left corner shows its curve.
            var layer = new Rectangle(0, 0, Width + 20, Height + 20);
            Theme.FillRounded(g, layer, Theme.RadiusLarge, Theme.Ground);
            Theme.DrawRounded(g, layer, Theme.RadiusLarge, Theme.Edge);
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
            new[] { "Pointing",             "Arrow", "UpArrow", "Hand", "Help" },
            new[] { "Text and drawing",     "IBeam", "NWPen", "Crosshair" },
            new[] { "Busy and unavailable", "Wait", "AppStarting", "No" },
            new[] { "Resizing and moving",  "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll" },
            new[] { "Places and people",    "Pin", "Person" }
        };

        internal static readonly Dictionary<string, string> Display = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Arrow",       "Normal select" },
            { "Help",        "Help select" },
            { "AppStarting", "Working in background" },
            { "Wait",        "Busy" },
            { "Crosshair",   "Precision select" },
            { "IBeam",       "Text select" },
            { "NWPen",       "Handwriting" },
            { "No",          "Unavailable" },
            { "SizeNS",      "Vertical resize" },
            { "SizeWE",      "Horizontal resize" },
            { "SizeNWSE",    "Diagonal resize 1" },
            { "SizeNESW",    "Diagonal resize 2" },
            { "SizeAll",     "Move" },
            { "UpArrow",     "Alternate select" },
            { "Hand",        "Link select" },
            { "Pin",         "Location select" },
            { "Person",      "Person select" }
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

    /// <summary>A closer look at one cursor: 128px, animated, with its file, in the shape of a WinUI dialog.</summary>
    internal sealed class CursorDetail : Form
    {
        private const int Footer = 80;
        private readonly IntPtr _handle;
        private readonly bool _dark;
        private readonly Point _centring;
        private readonly Timer _timer = new Timer();
        private int _step;
        private readonly int _frames;

        internal CursorDetail(string role, string file, IntPtr handle, int frames)
        {
            _handle = handle;
            if (handle != IntPtr.Zero)
                using (var art = CursorArt.Render(handle, 128))
                {
                    _dark = CursorArt.IsDark(art);
                    _centring = CursorArt.Centring(art);
                }
            Text = role;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 330);
            BackColor = Theme.Panel;
            Font = Theme.FontBody;
            ShowInTaskbar = false;

            var name = new Label
            {
                Text = role, Font = Theme.FontSubtitle, ForeColor = Theme.Text, BackColor = Color.Transparent,
                AutoSize = false, Bounds = new Rectangle(24, 178, 352, 30)
            };
            var lines = new Label
            {
                Bounds = new Rectangle(24, 210, 352, 40),
                ForeColor = Theme.Dim,
                BackColor = Color.Transparent,
                AutoSize = false,
                AutoEllipsis = true,
                Text = Describe(file, frames)
            };

            int top = ClientSize.Height - Footer + 24;
            var folder = new FlatButton("Show the file", false) { Bounds = new Rectangle(24, top, 172, 32), BackColor = Theme.Ground2 };
            folder.Enabled = file != null && System.IO.File.Exists(file);
            folder.Click += delegate
            {
                // /select, deschide folderul cu fisierul deja evidentiat.
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\"");
            };
            var close = new FlatButton("Close", false) { Bounds = new Rectangle(204, top, 172, 32), BackColor = Theme.Ground2 };
            close.Click += delegate { Close(); };

            Controls.Add(name);
            Controls.Add(lines);
            Controls.Add(folder);
            Controls.Add(close);

            _timer.Interval = 90;
            _frames = frames;
            _timer.Tick += delegate { if (_frames > 1) { _step = (_step + 1) % _frames; Invalidate(new Rectangle(0, 0, ClientSize.Width, 170)); } };
            _timer.Start();
            FormClosed += delegate { _timer.Stop(); };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private static string Describe(string file, int frames)
        {
            if (file == null || !System.IO.File.Exists(file)) return "No file of its own: Windows draws its default.";
            var info = new System.IO.FileInfo(file);
            string kind = frames > 1 ? frames + " frames" : "1 frame";
            return info.Name + "\n" + (info.Length / 1024.0).ToString("0.#") + " KB  ·  " + kind;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            if (Native.DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            int round = 2;
            Native.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            int caption = Theme.Panel.B << 16 | Theme.Panel.G << 8 | Theme.Panel.R;
            Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Panel);
            using (var footer = new SolidBrush(Theme.Ground2))
                g.FillRectangle(footer, 0, ClientSize.Height - Footer, ClientSize.Width, Footer);
            using (var line = new Pen(Theme.Edge))
                g.DrawLine(line, 0, ClientSize.Height - Footer, ClientSize.Width, ClientSize.Height - Footer);
            if (_handle == IntPtr.Zero) return;

            var box = new Rectangle((ClientSize.Width - 128) / 2, 28, 128, 128);
            if (_dark)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.FillRounded(g, Rectangle.Inflate(box, 8, 8), Theme.RadiusLarge, Theme.Swatch);
                g.SmoothingMode = SmoothingMode.Default;
            }
            IntPtr hdc = g.GetHdc();
            Native.DrawIconEx(hdc, box.X + _centring.X, box.Y + _centring.Y, _handle, 128, 128, _step, IntPtr.Zero, Native.DI_NORMAL);
            g.ReleaseHdc(hdc);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly string _libraryDir;
        private readonly string _backupFile;

        private readonly SchemeList _list = new SchemeList();
        private readonly CardPanel _flow = new CardPanel();
        private readonly Label _title = new Label();
        private readonly Label _subtitle = new Label();
        private readonly LinkLabel _status = new LinkLabel();
        private readonly FlatButton _apply = new FlatButton("Apply", true);
        private readonly FlatButton _restore = new FlatButton("Restore my cursors", false);
        private readonly FlatButton _more = new FlatButton("", false) { Subtle = true, Glyph = "", AccessibleName = "More options" };
        private readonly Timer _timer = new Timer();
        private readonly Timer _confirm = new Timer();
        private readonly Timer _updates = new Timer { Interval = 5000 };
        private readonly FlatButton _pizza = new FlatButton("🍕 Buy me a pizza", false) { AccessibleName = "Buy me a pizza", Pizza = true, Font = Theme.FontBodyStrong };
        private readonly Timer _nudge = new Timer { Interval = 3000 };
        private readonly Timer _nudgeFrame = new Timer { Interval = 15 };
        private readonly System.Diagnostics.Stopwatch _nudgeClock = new System.Diagnostics.Stopwatch();
        private bool _checking;

        private readonly Dictionary<string, RoleTile> _tiles = new Dictionary<string, RoleTile>(StringComparer.Ordinal);
        private List<IntPtr> _liveHandles = new List<IntPtr>();
        private int _hoverIndex = -1, _leaveIndex = -1;
        private readonly Fade _hoverFade, _leaveFade, _reveal;
        private string _activeName = "";
        private readonly SearchBox _searchBox = new SearchBox();
        private readonly TextBox _search;
        private readonly List<Scheme> _schemes = new List<Scheme>();
        private readonly Dictionary<string, Bitmap> _thumbs = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private int _firstSystem = -1;
        private int _libraryShown, _systemShown;

        internal MainForm()
        {
            _libraryDir = DataFolder.Library;
            _backupFile = DataFolder.Backup;
            if (!Directory.Exists(_libraryDir)) Directory.CreateDirectory(_libraryDir);

            _search = _searchBox.Field;
            _hoverFade = new Fade(110, delegate { InvalidateRow(_hoverIndex); });
            _leaveFade = new Fade(110, delegate { InvalidateRow(_leaveIndex); });
            _reveal = new Fade(150, delegate
            {
                foreach (var tile in _tiles.Values) { tile.Reveal = _reveal.Value; tile.Invalidate(); }
            });

            Text = "Cursor Selector";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Size = new Size(1240, 920);
            MinimumSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.FontBody;
            BackColor = Theme.Ground2;

            BuildLeftPanel();
            BuildRightPanel();
            BuildTiles();

            _confirm.Interval = 3000;
            _confirm.Tick += delegate { _confirm.Stop(); ShowActive(); };

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

            // Pizza pulseaza de doua ori la 3 s dupa deschidere, apoi la fiecare 10 s, ca sa fie observat.
            // Minimizata, nu ruleaza deloc.
            _nudge.Tick += delegate { _nudge.Interval = 10000; Nudge(); };
            _nudgeFrame.Tick += delegate { NudgeFrame(); };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized) { _nudge.Stop(); _nudgeFrame.Stop(); _pizza.Glow = FlatButton.PizzaRed; }
                else if (Visible) _nudge.Start();
            };

            Shown += delegate { RefreshList(null); _timer.Start(); _updates.Start(); _nudge.Start(); };
            FormClosed += delegate
            {
                _timer.Stop();
                foreach (var h in _liveHandles) Native.DestroyCursor(h);
                foreach (var b in _thumbs.Values) if (b != null) b.Dispose();
            };
        }

        // Fara puls cand "Animation effects" e oprit in Windows sau mouse-ul sta pe buton.
        private void Nudge()
        {
            if (!Theme.AnimationsOn || _pizza.Hovered) return;
            _nudgeClock.Restart();
            _nudgeFrame.Start();
        }

        // WinForms nu poate scala un control, deci doar culoarea: doua varfuri spre #F25555 in 700 ms,
        // fiecare segment cu ease-out cubic.
        private void NudgeFrame()
        {
            double ms = _nudgeClock.Elapsed.TotalMilliseconds;
            if (ms >= 700 || _pizza.Hovered)
            {
                _nudgeFrame.Stop();
                _pizza.Glow = FlatButton.PizzaRed;
                _pizza.Invalidate();
                return;
            }
            double p = (ms % 175) / 175, eased = 1 - Math.Pow(1 - p, 3);
            double k = (int)(ms / 175) % 2 == 0 ? eased : 1 - eased;      // urca spre varf, apoi coboara
            Color a = FlatButton.PizzaRed, b = Color.FromArgb(0xF2, 0x55, 0x55);
            _pizza.Glow = Color.FromArgb(a.R + (int)((b.R - a.R) * k), a.G + (int)((b.G - a.G) * k), a.B + (int)((b.B - a.B) * k));
            _pizza.Invalidate();
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

            // Bara de titlu in griul panoului cu lista, ca in aplicatiile Windows 11 (0x00BBGGRR).
            int caption = Theme.Ground2.B << 16 | Theme.Ground2.G << 8 | Theme.Ground2.R;
            Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        }

        private Scheme Selected { get { return _list.SelectedItem as Scheme; } }

        private void ShowFiles()
        {
            var scheme = Selected;
            if (scheme != null && scheme.Location != null && Directory.Exists(scheme.Location))
                System.Diagnostics.Process.Start("explorer.exe", scheme.Location);
        }

        private void RemoveSelected()
        {
            var scheme = Selected;
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
        }

        private static void OpenLink(string url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <summary>Click dreapta pe o schema: o aplica, arata fisierele ei, sau o scoate din biblioteca.</summary>
        private void BuildListMenu()
        {
            var menu = DarkMenu.Build();
            var apply = new ToolStripMenuItem("Apply") { Font = Theme.FontBodyStrong };
            var show = new ToolStripMenuItem("Show the files");
            var remove = new ToolStripMenuItem("Remove from library…");
            menu.Items.Add(apply);
            menu.Items.Add(show);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(remove);

            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                var scheme = Selected;
                if (scheme == null) { e.Cancel = true; return; }
                apply.Enabled = _apply.Enabled;
                show.Enabled = scheme.Location != null && Directory.Exists(scheme.Location);
                // Stergem doar ce am copiat noi; o schema inregistrata in Windows nu e a noastra.
                remove.Enabled = scheme.FromLibrary;
            };
            apply.Click += OnApply;
            show.Click += delegate { ShowFiles(); };
            remove.Click += delegate { RemoveSelected(); };

            // Click dreapta muta si selectia, altfel meniul ar lucra pe alt rand decat cel aratat.
            _list.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                int index = _list.IndexFromPoint(e.Location);
                if (index >= 0 && index < _list.Items.Count) _list.SelectedIndex = index;
            };
            _list.ContextMenuStrip = menu;
        }

        /// <summary>The ⋯ button: what used to be a stack of buttons under the list, plus the app's own items.</summary>
        private void BuildMoreMenu()
        {
            var menu = DarkMenu.Build();
            var show = new ToolStripMenuItem("Show the files");
            var remove = new ToolStripMenuItem("Remove from library…");
            var openLibrary = new ToolStripMenuItem("Open library folder");
            var refresh = new ToolStripMenuItem("Refresh list");
            var update = new ToolStripMenuItem("Check for updates");
            var more = new ToolStripMenuItem("More apps from Protagonist Labs");
            menu.Items.AddRange(new ToolStripItem[]
            {
                show, remove, new ToolStripSeparator(), openLibrary, refresh, new ToolStripSeparator(), update, more
            });

            menu.Opening += delegate
            {
                var scheme = Selected;
                show.Enabled = scheme != null && scheme.Location != null && Directory.Exists(scheme.Location);
                remove.Enabled = scheme != null && scheme.FromLibrary;
                update.Text = "Check for updates  (" + Program.Version + ")";
            };
            show.Click += delegate { ShowFiles(); };
            remove.Click += delegate { RemoveSelected(); };
            openLibrary.Click += delegate { System.Diagnostics.Process.Start("explorer.exe", _libraryDir); };
            refresh.Click += delegate { RefreshList(null); };
            update.Click += delegate { CheckForUpdates(true); };
            more.Click += delegate { OpenLink("https://protagonistlabs.app/?utm_source=cursorselector&utm_medium=app&utm_campaign=more-apps"); };

            _more.Click += delegate
            {
                menu.Show(_more, new Point(_more.Width, _more.Height + 4), ToolStripDropDownDirection.BelowLeft);
            };
        }

        private void BuildLeftPanel()
        {
            var left = new Panel
            {
                Dock = DockStyle.Left,
                Width = 304,
                Padding = new Padding(12, 12, 8, 14),
                BackColor = Theme.Ground2
            };
            const int inner = 304 - 12 - 8;

            _list.Dock = DockStyle.Fill;
            // Variabila, fiindca primul rand al fiecarei sectiuni isi poarta deasupra titlul ei.
            _list.DrawMode = DrawMode.OwnerDrawVariable;
            _list.MeasureItem += delegate(object sender, MeasureItemEventArgs e)
            {
                e.ItemHeight = SectionStart(e.Index) ? RowHeight + SectionHead : RowHeight;
            };
            _list.ItemHeight = RowHeight;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Ground2;
            _list.DrawItem += DrawSchemeRow;
            _list.SelectedIndexChanged += delegate { ShowScheme(Selected); };
            _list.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int index = _list.IndexFromPoint(e.Location);
                if (index != _hoverIndex) HoverRow(index);
            };
            _list.MouseLeave += delegate { HoverRow(-1); };
            // Dublu click sau Enter pe o schema o aplica, ca intr-o lista din Windows.
            _list.DoubleClick += delegate
            {
                int index = _list.IndexFromPoint(_list.PointToClient(MousePosition));
                if (index >= 0 && index == _list.SelectedIndex) OnApply(this, EventArgs.Empty);
            };
            _list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                OnApply(this, EventArgs.Empty);
                e.Handled = e.SuppressKeyPress = true;
            };
            _list.HandleCreated += delegate { Native.SetWindowTheme(_list.Handle, "DarkMode_Explorer", null); };
            BuildListMenu();

            // Cautare, si importul ca un + langa ea, in locul celor trei butoane mari de sub lista.
            var searchRow = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(0, 0, 0, 14), BackColor = Theme.Ground2 };
            _searchBox.Dock = DockStyle.Fill;
            _search.TextChanged += delegate { FillList(null); };
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
            var import = new FlatButton("", false)
            {
                Subtle = true, Glyph = "", AccessibleName = "Import a cursor pack",
                Dock = DockStyle.Right, Width = 32, BackColor = Theme.Ground2
            };
            import.Click += OnImport;
            new ToolTip().SetToolTip(import, "Import a cursor pack (.zip or folder). You can also drop one on the window.");
            searchRow.Controls.Add(_searchBox);
            searchRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6, BackColor = Theme.Ground2 });
            searchRow.Controls.Add(import);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Ground2,
                Padding = new Padding(0, 12, 0, 0)
            };
            _pizza.Size = new Size(inner, 32);
            _pizza.Margin = new Padding(0, 0, 0, 10);
            _pizza.BackColor = Theme.Ground2;
            _pizza.Click += delegate { OpenLink("https://buymeacoffee.com/protagonistlabs"); };
            new ToolTip().SetToolTip(_pizza, "Optional. It goes toward code signing and a Steam listing.");

            const string mail = "Feedback", site = "Website", news = "What's new";
            var links = Hyperlinks(mail + "     " + site + "     " + news, inner);
            links.Links.Add(0, mail.Length, "mailto:feedback@protagonistlabs.app?subject=Cursor%20Selector%20feedback");
            links.Links.Add(links.Text.IndexOf(site), site.Length, "https://protagonistlabs.app/cursorselector/?utm_source=cursorselector&utm_medium=app&utm_campaign=sidebar");
            links.Links.Add(links.Text.IndexOf(news), news.Length, "whatsnew");
            LinkLabelLinkClickedEventHandler open = delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                if ((string)e.Link.LinkData == "whatsnew") { WhatsNewForm.Open(this); return; }
                OpenLink((string)e.Link.LinkData);
            };
            links.LinkClicked += open;
            new ToolTip().SetToolTip(links, "feedback@protagonistlabs.app");
            bottom.Controls.Add(_pizza);
            bottom.Controls.Add(links);

            // "Works with": nicio aplicatie sora nu adauga ceva aici, deci ramane doar randul cu
            // reducerea, si numai pentru cine a cumparat deja una. In culoarea textului din jur, ca restul.
            var offer = Ecosystem.Offer();
            if (offer.Count > 0)
            {
                const string lead = "Owners of one of our apps get 30% off the others: ";
                var others = Hyperlinks(lead + string.Join(" · ", offer.Select(o => o.Key)), inner);
                others.Size = new Size(inner, 40);
                others.Margin = new Padding(0, 6, 0, 0);
                int at = lead.Length;
                foreach (var o in offer)
                {
                    others.Links.Add(at, o.Key.Length, o.Value);
                    at += o.Key.Length + 3;
                }
                others.LinkClicked += open;
                bottom.Controls.Add(others);
            }

            // Controlul cu Dock=Fill se adauga primul: docarea merge de la ultimul adaugat spre primul.
            left.Controls.Add(_list);
            left.Controls.Add(searchRow);
            left.Controls.Add(bottom);
            Controls.Add(left);
        }

        private static LinkLabel Hyperlinks(string text, int width)
        {
            var label = new LinkLabel
            {
                Text = text,
                AutoSize = false,
                Size = new Size(width, 22),
                Margin = new Padding(0),
                Font = Theme.FontSmall,
                ForeColor = Theme.Dimmer,
                LinkColor = Theme.AccentText,
                ActiveLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                TextAlign = ContentAlignment.MiddleCenter
            };
            label.Links.Clear();
            return label;
        }

        /// <summary>
        /// Randul de sub mouse se aprinde treptat, iar cel parasit se stinge la fel, ca in listele din Windows 11.
        /// Redesenam doar randul atins si fara stergerea fundalului: Invalidate() pe tot ListBox-ul
        /// il golea intai cu fundalul si apoi redesena fiecare rand, de unde flicker-ul la hover.
        /// </summary>
        private void HoverRow(int index)
        {
            if (index == _hoverIndex) return;
            int old = _leaveIndex;
            _leaveIndex = _hoverIndex;
            InvalidateRow(old);
            _leaveFade.Run(_hoverFade.Value, 0);
            _hoverIndex = index;
            _hoverFade.Run(0, 1);
        }

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

        private const int RowHeight = 40, SectionHead = 40;

        /// <summary>The first row of each section carries the section's heading above it.</summary>
        private bool SectionStart(int index) { return index == 0 || index == _firstSystem; }

        private void DrawSchemeRow(Graphics g, DrawItemEventArgs e)
        {
            var scheme = (Scheme)_list.Items[e.Index];
            // Nu folosim Graphics.Clear: umple toata suprafata controlului, nu doar randul curent.
            using (var background = new SolidBrush(Theme.Ground2))
                g.FillRectangle(background, e.Bounds);

            var bounds = e.Bounds;
            if (SectionStart(e.Index))
            {
                // Titlul sectiunii, ca in Setari, cu cate scheme sunt sub el.
                var head = new Rectangle(bounds.X + 12, bounds.Y, bounds.Width - 30, SectionHead - 8);
                TextRenderer.DrawText(g, scheme.FromLibrary ? "Your library" : "Installed in Windows", Theme.FontBodyStrong,
                    head, Theme.Text, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, (scheme.FromLibrary ? _libraryShown : _systemShown).ToString(), Theme.FontSmall,
                    head, Theme.Dimmer, TextFormatFlags.Right | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
                bounds = new Rectangle(bounds.X, bounds.Y + SectionHead, bounds.Width, bounds.Height - SectionHead);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 10, bounds.Height - 4);
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            float hover = e.Index == _hoverIndex ? _hoverFade.Value : e.Index == _leaveIndex ? _leaveFade.Value : 0;
            Color fill = selected
                ? Theme.Mix(Theme.SelectedRow, Theme.Over(Theme.Ground2, 0.0837), hover)
                : Theme.Mix(Theme.Ground2, Theme.PanelOnSide, hover);
            if (fill != Theme.Ground2) Theme.FillRounded(g, r, Theme.Radius, fill);
            if (selected)
                using (var brush = new SolidBrush(Theme.Accent))
                using (var path = Theme.RoundedPath(new Rectangle(r.X, r.Y + (r.Height - 16) / 2, 3, 16), 2))
                    g.FillPath(brush, path);

            // Trei cursoare ale schemei (normal, link, busy): numele singur nu deosebeste Amber de Ice.
            int right = r.Right - 8;
            var strip = Thumb(scheme);
            if (strip != null)
            {
                g.DrawImageUnscaled(strip, right - strip.Width, r.Y + (r.Height - strip.Height) / 2);
                right -= strip.Width + 10;
            }
            if (scheme.Name == _activeName)
            {
                const string tag = "In use";
                int width = TextRenderer.MeasureText(g, tag, Theme.FontSmall, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, tag, Theme.FontSmall, new Rectangle(right - width, r.Y, width, r.Height), Theme.AccentText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                right -= width + 10;
            }

            var nameRect = new Rectangle(r.X + 14, r.Y, right - r.X - 14, r.Height);
            TextRenderer.DrawText(g, scheme.Name, Theme.FontBody, nameRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static readonly string[] ThumbRoles = { "Arrow", "Hand", "Wait" };
        private const int ThumbCell = 22, ThumbGap = 4;

        /// <summary>
        /// Cursoarele unei scheme din lista, desenate o singura data intr-o imagine: zeci de randuri ar
        /// reincarca aceleasi fisiere la fiecare redesenare, si ar tine sute de cursoare deschise.
        /// </summary>
        private Bitmap Thumb(Scheme scheme)
        {
            Bitmap strip;
            if (_thumbs.TryGetValue(scheme.Name, out strip)) return strip;

            strip = new Bitmap(ThumbRoles.Length * (ThumbCell + ThumbGap) - ThumbGap, ThumbCell);
            bool any = false;
            using (var g = Graphics.FromImage(strip))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                for (int i = 0; i < ThumbRoles.Length; i++)
                {
                    string path;
                    if (!scheme.Map.TryGetValue(ThumbRoles[i], out path) || string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    IntPtr handle = Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_CURSOR, 32, 32, Native.LR_LOADFROMFILE);
                    if (handle == IntPtr.Zero) handle = Native.LoadCursorFromFile(path);
                    if (handle == IntPtr.Zero) continue;
                    using (var art = CursorArt.Render(handle, 32))
                    {
                        var cell = new Rectangle(i * (ThumbCell + ThumbGap), 0, ThumbCell, ThumbCell);
                        if (CursorArt.IsDark(art)) Theme.FillRounded(g, new Rectangle(cell.X, cell.Y, cell.Width - 1, cell.Height - 1), Theme.Radius, Theme.Swatch);
                        // Desenul cursorului, micsorat la 20 din 32 si pus in mijlocul celulei.
                        var drawn = CursorArt.Bounds(art);
                        int w = drawn.Width * 20 / 32, h = drawn.Height * 20 / 32;
                        g.DrawImage(art, new Rectangle(cell.X + (cell.Width - w) / 2, cell.Y + (cell.Height - h) / 2, w, h), drawn, GraphicsUnit.Pixel);
                    }
                    Native.DestroyCursor(handle);
                    any = true;
                }
            }
            if (!any) { strip.Dispose(); strip = null; }
            _thumbs[scheme.Name] = strip;
            return strip;
        }

        private void BuildRightPanel()
        {
            var right = new ComposedPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(36, 26, 28, 6),
                BackColor = Theme.Ground
            };

            // Numele schemei, cu actiunile ei langa el: Apply nu mai sta jos, departe de ce aplica.
            var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Ground };
            var commands = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Ground,
                Padding = new Padding(0, 6, 0, 0)
            };
            _more.Size = new Size(36, 32);
            _more.Margin = new Padding(6, 0, 0, 0);
            _restore.Size = new Size(164, 32);
            _restore.Margin = new Padding(8, 0, 0, 0);
            _apply.Size = new Size(110, 32);
            _apply.Margin = new Padding(16, 0, 0, 0);
            _apply.Click += OnApply;
            _restore.Click += OnRestore;
            new ToolTip().SetToolTip(_restore, "Put back the cursors you had before Cursor Selector first changed them.");
            new ToolTip().SetToolTip(_more, "More options");
            BuildMoreMenu();
            commands.Controls.AddRange(new Control[] { _more, _restore, _apply });

            var names = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ground };
            _title.Dock = DockStyle.Top;
            _title.Height = 42;
            _title.Text = "Select a scheme";
            _title.Font = Theme.FontDisplay;
            _title.ForeColor = Theme.Text;
            _title.AutoEllipsis = true;
            _title.UseMnemonic = false;

            _subtitle.Dock = DockStyle.Top;
            _subtitle.Height = 24;
            _subtitle.ForeColor = Theme.Dim;
            _subtitle.AutoEllipsis = true;
            names.Controls.Add(_subtitle);
            names.Controls.Add(_title);

            header.Controls.Add(names);
            header.Controls.Add(commands);

            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = "Point at a cursor to try it. Click it for a closer look.",
                ForeColor = Theme.Dimmer,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _flow.Dock = DockStyle.Fill;
            // Doar pe verticala: placile se rearanjeaza pe randuri, deci n-au ce ascunde in dreapta. WinForms
            // calcula zona de derulare cu latimile dinainte de redimensionare si arata o bara orizontala.
            _flow.HorizontalScroll.Maximum = 0;
            _flow.HorizontalScroll.Visible = false;
            _flow.AutoScroll = true;
            _flow.FlowDirection = FlowDirection.TopDown;
            _flow.WrapContents = false;
            _flow.Padding = new Padding(0, 0, 0, 16);
            _flow.HandleCreated += delegate { Native.SetWindowTheme(_flow.Handle, "DarkMode_Explorer", null); };
            _flow.Resize += delegate { SizeGroups(); };

            // Bara de stare, ca in Explorer: schema din Windows (click = sari la ea) si confirmarile.
            _status.Dock = DockStyle.Bottom;
            _status.Height = 34;
            _status.Font = Theme.FontSmall;
            _status.ForeColor = Theme.Dimmer;
            _status.LinkColor = Theme.AccentText;
            _status.ActiveLinkColor = Theme.Accent;
            _status.LinkBehavior = LinkBehavior.HoverUnderline;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.AutoEllipsis = true;
            _status.LinkClicked += delegate { JumpToActive(); };

            right.Controls.Add(_flow);
            right.Controls.Add(hint);
            right.Controls.Add(header);
            right.Controls.Add(_status);

            Controls.Add(right);
            right.BringToFront();
        }

        private void BuildTiles()
        {
            // Fiecare grup sub titlul lui, cu placile pe randuri care se rearanjeaza dupa latimea ferestrei.
            foreach (var group in Roles.Groups)
            {
                _flow.Controls.Add(new Label
                {
                    Text = group[0],
                    Font = Theme.FontBodyStrong,
                    ForeColor = Theme.Text,
                    AutoSize = false,
                    Height = 38,
                    TextAlign = ContentAlignment.BottomLeft,
                    Margin = new Padding(0, 0, 0, 8)
                });

                var cards = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0),
                    BackColor = Theme.Ground
                };
                for (int i = 1; i < group.Length; i++)
                {
                    var tile = new RoleTile(group[i], Roles.Display[group[i]]);
                    var captured = tile;
                    tile.Click += delegate { ShowCursorDetail(captured); };
                    cards.Controls.Add(tile);
                    _tiles[group[i]] = tile;
                }
                _flow.Controls.Add(cards);
            }
            SizeGroups();
        }

        private void SizeGroups()
        {
            // Locul barei verticale e pastrat mereu: cand apare, nu mai impinge placile sub o bara orizontala.
            int width = Math.Max(RoleTile.W + 4, _flow.Width - _flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            foreach (Control c in _flow.Controls)
            {
                if (c is FlowLayoutPanel) c.MaximumSize = new Size(width, 0);
                else c.Width = width;
            }
        }

        private void ShowCursorDetail(RoleTile tile)
        {
            // Incarcat din nou la 128px: marit din cel de 40px al placii, iesea in trepte.
            IntPtr big = tile.File == null || !File.Exists(tile.File) ? IntPtr.Zero
                : Native.LoadImage(IntPtr.Zero, tile.File, Native.IMAGE_CURSOR, 128, 128, Native.LR_LOADFROMFILE);
            using (var detail = new CursorDetail(tile.Text, tile.File, big != IntPtr.Zero ? big : tile.CursorHandle, CursorArt.Frames(tile.File)))
                detail.ShowDialog(this);
            if (big != IntPtr.Zero) Native.DestroyCursor(big);
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

                IntPtr handle = IntPtr.Zero, live = IntPtr.Zero;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    handle = Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_CURSOR, RoleTile.Box, RoleTile.Box, Native.LR_LOADFROMFILE);
                    if (handle == IntPtr.Zero) handle = Native.LoadCursorFromFile(path);   // unele .ani vechi nu trec prin LoadImage
                    live = Native.LoadCursorFromFile(path);   // la marimea pointerului din Windows, cu animatie
                }
                if (handle != IntPtr.Zero) current.Add(handle);
                if (live != IntPtr.Zero) current.Add(live);

                _tiles[role].Reset(handle, live, path);
            }

            // Fiecare incarcare creeaza un handle nou si procesul are o limita (~300 cursoare
            // simultan), asa ca eliberam schema anterioara acum ca placile refera handle-urile noi.
            _liveHandles = current;
            foreach (var h in previous) Native.DestroyCursor(h);
            _reveal.Run(0.35f, 1);

            if (scheme == null)
            {
                _title.Text = "Select a scheme";
                _subtitle.Text = "";
                return;
            }

            _title.Text = scheme.Name;
            int total = Roles.Order.Length, missing = total - scheme.Map.Count;
            _subtitle.Text = (scheme.FromLibrary ? "Your library" : "Installed in Windows") + "  ·  "
                + (missing == 0
                    ? "all " + total + " cursors"
                    : string.Format("{0} of {1} cursors, Windows draws the other {2}", scheme.Map.Count, total, missing));
        }

        /// <summary>Reciteste biblioteca; selecteaza schema din <paramref name="location"/> daca e data.</summary>
        private void RefreshList(string location)
        {
            foreach (var strip in _thumbs.Values) if (strip != null) strip.Dispose();
            _thumbs.Clear();

            _schemes.Clear();
            _schemes.AddRange(Library.GetAll(_libraryDir));
            FillList(location);
            SetActive(CursorConfig.ActiveName());
            _restore.Enabled = File.Exists(_backupFile);
        }

        /// <summary>Umple lista cu schemele care trec de filtrul de cautare, pastrand selectia.</summary>
        private void FillList(string location)
        {
            var previous = Selected;
            string previousName = previous == null ? null : previous.Name;
            string query = _search.Text.Trim();

            _hoverIndex = _leaveIndex = -1;
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
            _libraryShown = _firstSystem < 0 ? _list.Items.Count : _firstSystem;
            _systemShown = _list.Items.Count - _libraryShown;
            _list.EndUpdate();
            Native.SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);

            if (_list.Items.Count == 0)
            {
                _flow.SetEmpty(
                    _schemes.Count == 0 ? "No cursor schemes yet" : "Nothing matches that",
                    _schemes.Count == 0
                        ? "Drop a cursor folder or a .zip here, or use + beside the search."
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
            ShowScheme(Selected);
        }

        private void SetActive(string name)
        {
            _activeName = name;
            ShowActive();
            _list.Invalidate();
            UpdateApplyState();
        }

        /// <summary>The status bar at rest: the scheme Windows uses now, as a link that selects it in the list.</summary>
        private void ShowActive()
        {
            const string lead = "In use: ";
            bool listed = _schemes.Any(s => s.Name == _activeName);
            _status.ForeColor = Theme.Dimmer;
            _status.Text = lead + (_activeName.Length > 0 ? _activeName : "Windows default (no scheme)");
            _status.Links.Clear();
            if (listed) _status.Links.Add(lead.Length, _activeName.Length);
        }

        private void JumpToActive()
        {
            if (!_schemes.Any(s => s.Name == _activeName)) return;
            if (!_list.Items.Cast<Scheme>().Any(s => s.Name == _activeName)) _search.Clear();
            for (int i = 0; i < _list.Items.Count; i++)
                if (((Scheme)_list.Items[i]).Name == _activeName) { _list.SelectedIndex = i; break; }
            _list.Focus();
        }

        /// <summary>
        /// Butonul principal nu mai cheama la o actiune care nu schimba nimic: cand schema aleasa
        /// e chiar cea pusa, spune asta si se stinge.
        /// </summary>
        private void UpdateApplyState()
        {
            var scheme = Selected;
            bool applied = scheme != null && scheme.Name == _activeName;
            _apply.Text = applied ? "Applied" : "Apply";
            _apply.Enabled = scheme != null && !applied;
        }

        /// <summary>
        /// Pe un fir din pool: reteaua nu are voie sa blocheze fereastra. Verificarea automata tace cand nu
        /// e nimic nou sau cand n-a putut verifica (reincearca peste o ora); cea ceruta (din meniul ⋯)
        /// raspunde mereu, si spune altfel "nimic nou" decat "n-am putut verifica".
        /// </summary>
        private void CheckForUpdates(bool manual)
        {
            if (_checking) return;
            _checking = true;
            if (manual) Confirm("Checking for updates…");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool failed;
                var waiting = Updater.UpdateInPlace(out failed);
                BeginInvoke((Action)delegate
                {
                    _checking = false;
                    if (waiting != null) Confirm("Updated to " + waiting + " — the next start opens it");
                    else if (!manual) return;
                    else if (failed) Confirm("Couldn't check for updates — no connection or GitHub is busy. Try again later.");
                    else Confirm("You have the latest version (" + Program.Version + ")");
                });
            });
        }

        /// <summary>
        /// Confirmarea sta trei secunde in bara de stare, apoi randul revine la schema din Windows.
        /// </summary>
        private void Confirm(string message)
        {
            _status.ForeColor = Theme.Text;
            _status.Text = message;
            _status.Links.Clear();
            _confirm.Stop();
            _confirm.Start();
        }

        private void OnApply(object sender, EventArgs e)
        {
            var scheme = Selected;
            if (scheme == null || scheme.Name == _activeName) return;
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
                Confirm("Your own cursors are back");
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
                        { "version", Program.Version },
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
                Version latest, current = new Version(Program.Version);
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
            if (seen == Program.Version) return;
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
            using (var k = Registry.CurrentUser.CreateSubKey(SeenKey)) k.SetValue("WhatsNewSeen", Program.Version);
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

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Ground2 };
            var close = new FlatButton("Close", true) { Size = new Size(120, 32), BackColor = Theme.Ground2, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            close.Location = new Point(bottom.Width - close.Width - 24, 16);
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
            int current = _releases.FindIndex(r => r.Version == Program.Version);
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
            int caption = Theme.Ground2.B << 16 | Theme.Ground2.G << 8 | Theme.Ground2.R;
            Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
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
                if (_selected || _hover) Theme.FillRounded(g, box, Theme.Radius, _selected ? Theme.SelectedRow : Theme.PanelOnSide);
                if (_selected)
                    using (var brush = new SolidBrush(Theme.Accent))
                    using (var path = Theme.RoundedPath(new Rectangle(0, (Height - 16) / 2, 3, 16), 2))
                        g.FillPath(brush, path);
                if (Focused && ShowFocusCues) Theme.DrawFocus(g, box, Theme.Radius);
                var inner = Rectangle.Inflate(box, -12, 0);
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
                using (var clip = Theme.RoundedPath(all, Theme.RadiusLarge)) g.SetClip(clip);
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
        internal static readonly string Version = typeof(Program).Assembly.GetName().Version.ToString(3);

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
