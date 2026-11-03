using System;
using System.Drawing;
using System.Windows.Forms;

namespace CrosshairDesktop
{
    internal sealed class HotkeyCaptureDialog : Form, IMessageFilter
    {
        private readonly HotkeyService hotkeys;
        private readonly Label keys, hint;
        private readonly Button use;
        private bool started;
        private readonly ShortcutRecorder recorder = new ShortcutRecorder();
        internal KeyBinding Result;
        internal HotkeyCaptureDialog(HotkeyService hotkeys)
        {
            this.hotkeys = hotkeys;
            Text = "录入快捷键"; ClientSize = new Size(540, 250); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Theme.Card; ForeColor = Theme.Text; Font = new Font("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            keys = Theme.Label("请按下要使用的按键或组合键", 15, Theme.Accent);
            keys.AutoSize = false; keys.SetBounds(24, 25, 492, 75);
            hint = Theme.Label("全部松开后点击“使用此快捷键”。\n可录入单键或多个按键；Esc 也可作为快捷键。", 10, Theme.Muted);
            hint.AutoSize = false; hint.SetBounds(24, 105, 492, 68);
            use = Theme.Button("使用此快捷键", 180, () => { DialogResult = DialogResult.OK; Close(); }, true);
            use.Location = new Point(24, 194); use.Enabled = false;
            var cancel = Theme.Button("取消", 100, () => { DialogResult = DialogResult.Cancel; Close(); }, false);
            cancel.Location = new Point(214, 194);
            Controls.AddRange(new Control[] { keys, hint, use, cancel });
            Shown += (s, e) =>
            {
                try
                {
                    hotkeys.BeginCapture(Handle); recorder.Reset();
                    Application.AddMessageFilter(this); started = true;
                }
                catch (Exception ex) { hint.Text = ex.Message; }
            };
            Deactivate += (s, e) => { if (started && DialogResult == DialogResult.None) ResetRecording(); };
            FormClosed += (s, e) => StopRecording();
        }
        private void ResetRecording()
        {
            recorder.Reset(); Result = null; use.Enabled = false;
            keys.Text = "请按下要使用的按键或组合键";
            hint.Text = "请在此窗口录入；全部松开后点击“使用此快捷键”。";
        }
        private void StopRecording()
        {
            if (!started) return;
            Application.RemoveMessageFilter(this); started = false; hotkeys.EndCapture();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) StopRecording();
            base.Dispose(disposing);
        }
        public bool PreFilterMessage(ref Message message)
        {
            bool pressed = message.Msg == 0x100 || message.Msg == 0x104;
            bool released = message.Msg == 0x101 || message.Msg == 0x105;
            if (!started || (!pressed && !released)) return false;
            var control = Control.FromHandle(message.HWnd);
            if (message.HWnd != Handle && (control == null || control.FindForm() != this)) return false;
            long bits = message.LParam.ToInt64();
            int key = RawKeyboardWindow.Decode((ushort)message.WParam.ToInt32(), (ushort)((bits >> 16) & 0xFF), (ushort)(((bits >> 24) & 1) * 2));
            // Ignore repeat-down messages for keys held before the dialog opened.
            if (pressed && (bits & (1L << 30)) != 0) return true;
            if (recorder.Feed(key, pressed))
            {
                var candidate = recorder.Candidate;
                keys.Text = candidate.Label; Result = null; use.Enabled = false;
                if (!recorder.Ready) hint.Text = "正在录入，请松开全部按键…";
                else
                {
                    try { candidate.Validate(); Result = candidate; use.Enabled = true; hint.Text = "已录入。点击“使用此快捷键”，或直接按另一组键重新录入。"; }
                    catch (FormatException ex) { hint.Text = ex.Message; }
                }
            }
            return true;
        }
    }
}
