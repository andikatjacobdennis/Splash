using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PaintClone.Dialogs
{
    public partial class AboutDialog : Window
    {
        private const string MascotName = "Splash";

        private static readonly string[] FunFacts =
        {
            $"{MascotName} has spilled paint 2,847 times today. {MascotName} regrets nothing.",
            $"Fun fact: {MascotName} is not made of real paint. Please do not attempt to paint with {MascotName}.",
            $"{MascotName}'s favorite color is \"a little bit of everything.\"",
            $"Warning: {MascotName} gets a little too excited about the Fill With Color tool.",
            $"{MascotName} once tried to fill the whole canvas with himself. It did not go well.",
            $"Did you know? {MascotName} can't actually hold a paintbrush. He just supervises.",
            $"{MascotName} approves of your artistic choices. Mostly.",
            "Behind every great painting is a slightly nervous paint bucket.",
            $"{MascotName} believes every mistake is just an undiscovered brush stroke. Ctrl+Z exists anyway.",
            $"{MascotName} has strong feelings about the Eraser tool. He prefers not to discuss it.",
        };

        private static readonly Random Rng = new();

        /// <summary>Shown one at a time under the app name, picked at random each time the dialog
        /// opens - a small thing, but it makes About feel alive rather than static.</summary>
        private static readonly string[] Taglines =
        {
            "A cheerful little painting program",
            "Now with 100% more paint buckets",
            "Where straight lines come to be ignored",
            "Undo is right there, no judgement",
            "Powered by enthusiasm and a lot of pixels",
            "Every masterpiece starts as a wonky rectangle",
        };

        /// <summary>What Splash says when poked. Kept short so the bubble stays small.</summary>
        private static readonly string[] MascotSayings =
        {
            "Boop!", "Careful, I'm full!", "Hey!", "That tickles.",
            "Mind the paint...", "*sloshing noises*", "Again? Really?",
            "I'm 90% blue, 10% nerve.", "Ow. Worth it.", "Do that once more.",
        };

        private int _pokeCount;

        public AboutDialog()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
            RuntimeText.Text = Environment.Version.ToString();
            MascotNameText.Text = $"\"{MascotName}\"";

            try
            {
                MascotImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/Mascot/mascot.png", UriKind.Absolute));
            }
            catch
            {
                // Mascot is purely decorative - fine to leave blank if it can't load for some reason.
            }

            TaglineText.Text = Taglines[Rng.Next(Taglines.Length)];
            ShowRandomFact();
        }

        /// <summary>Poking the mascot makes him wobble and say something. Purely decorative - but
        /// an About box is exactly the right place for a bit of character.</summary>
        private void Mascot_Click(object sender, MouseButtonEventArgs e)
        {
            _pokeCount++;

            SpeechText.Text = _pokeCount >= 10
                ? "Okay, you clearly have time. Ten pokes! Have a sticker: \u2b50"
                : MascotSayings[Rng.Next(MascotSayings.Length)];
            SpeechBubble.Visibility = Visibility.Visible;

            // A quick wobble: over and back twice, easing out so it settles rather than stopping dead.
            var wobble = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(-9, KeyTime.FromPercent(0.20)));
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(7, KeyTime.FromPercent(0.45)));
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(-4, KeyTime.FromPercent(0.70)));
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            MascotRotate.BeginAnimation(RotateTransform.AngleProperty, wobble);
        }

        private void ShowRandomFact() => FunFactText.Text = FunFacts[Rng.Next(FunFacts.Length)];

        private void AnotherFact_Click(object sender, RoutedEventArgs e) => ShowRandomFact();
    }
}
