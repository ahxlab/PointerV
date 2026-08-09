using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace PointerV
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CircleForm());
        }
    }

    public class CircleForm : Form
    {
        // Win32 API 定数
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const uint ULW_ALPHA = 0x02;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
            public POINT(int x, int y) { this.x = x; this.y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        private readonly Timer timer;
        private readonly NotifyIcon notifyIcon;

        // 円の設定パラメータ
        private readonly int circleDiameter = 60; // 直径 60px
        // ライトグリーン & 透過50% (Alpha: 128 / 255)
        private readonly Color circleColor = Color.FromArgb(128, Color.LightGreen);

        private Bitmap? circleBitmap;

        public CircleForm()
        {
            // フォームの基本プロパティ設定
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(circleDiameter, circleDiameter);
            this.TopMost = true;
            this.ShowInTaskbar = false;

            // 【修正点】EXEファイルに埋め込まれたアイコンを直接抽出して読み込む
            Icon appIcon = SystemIcons.Application;
            try
            {
                appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                // 取得失敗時はデフォルトアイコン
            }

            this.Icon = appIcon;

            // タスクトレイアイコンの設定
            notifyIcon = new NotifyIcon
            {
                Icon = appIcon,
                Text = "PointerV",
                Visible = true
            };

            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Exit (&X)", null, (s, e) => Application.Exit());
            notifyIcon.ContextMenuStrip = contextMenu;

            // メモリ上にアルファチャンネル付きの円を描画
            RenderCircleBitmap();

            // 低遅延タイマー設定 (~10ms周期)
            timer = new Timer
            {
                Interval = 10
            };
            timer.Tick += Timer_Tick;
            timer.Start();
        }
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // クリック透過・レイヤード化・Alt+Tab非表示スタイルを登録
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);

            UpdateWindowBitmap(this.Location);
        }

        private void RenderCircleBitmap()
        {
            circleBitmap?.Dispose();
            circleBitmap = new Bitmap(circleDiameter, circleDiameter, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(circleBitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (SolidBrush brush = new SolidBrush(circleColor))
                {
                    g.FillEllipse(brush, 0, 0, circleDiameter - 1, circleDiameter - 1);
                }
            }
        }

        private void UpdateWindowBitmap(Point location)
        {
            if (circleBitmap == null || !this.IsHandleCreated) return;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = circleBitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                SIZE size = new SIZE(circleDiameter, circleDiameter);
                POINT pointSource = new POINT(0, 0);
                POINT pointTarget = new POINT(location.X, location.Y);

                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255, // アルファチャンネルを直接適用
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(this.Handle, screenDc, ref pointTarget, ref size, memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Shift + Alt キー判定
            bool isShiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool isAltPressed = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            if (isShiftPressed && isAltPressed)
            {
                if (this.Visible)
                {
                    this.Visible = false;
                }
                return;
            }

            if (!this.Visible)
            {
                this.Visible = true;
            }

            // カーソル位置へフォーム座標を追従更新
            Point cursorPosition = Cursor.Position;
            int newX = cursorPosition.X - (circleDiameter / 2);
            int newY = cursorPosition.Y - (circleDiameter / 2);

            if (this.Location.X != newX || this.Location.Y != newY)
            {
                this.Location = new Point(newX, newY);
                UpdateWindowBitmap(this.Location);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timer.Stop();
            timer.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            circleBitmap?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
