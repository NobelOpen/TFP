using System;
using System.Drawing;
using System.Windows.Forms;

namespace TextDummy
{
    public class MainForm : Form
    {
        private System.Windows.Forms.Timer _timer;
        private string[] _dialogues = new string[]
        {
            "「あのね、私のこと、どう思ってる？」",
            "「えっと……本当に言っていいの？」",
            "「もちろん！ 遠慮しないで教えてよ」",
            "「実は……ずっと好きだと思ってた」",
            "「……えっ？ うそ、本当に？」",
            "「本当だよ。ずっと君の事を見てた」",
            "「ばか……もっと早く言ってくれれば良かったのに」",
            "「……これから、ずっと一緒にいてくれる？」"
        };
        private int _currentIndex = 0;
        private string _currentText = "クリックかエンターで開始 (Click or Enter start)";

        public MainForm()
        {
            this.Text = "TextDummy - Galgame Simulator";
            this.Size = new Size(600, 200);
            this.DoubleBuffered = true;
            this.Font = new Font("MS Gothic", 24, FontStyle.Regular);
            this.BackColor = Color.Black;
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterScreen;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 3000;
            _timer.Tick += (s, e) =>
            {
                if (_currentIndex < _dialogues.Length)
                {
                    _currentText = _dialogues[_currentIndex];
                    _currentIndex++;
                    this.Invalidate(); 
                }
                else
                {
                    _currentIndex = 0;
                }
            };
            
            this.MouseDown += (s, e) => StartDialogue();
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                    StartDialogue();
            };
        }

        private void StartDialogue()
        {
            if (!_timer.Enabled) 
            {
                _timer.Start();
                _currentIndex = 0;
                _currentText = _dialogues[_currentIndex++];
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, _currentText, this.Font, new Point(20, 60), this.ForeColor);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
    }
}
