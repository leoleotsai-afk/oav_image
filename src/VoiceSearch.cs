using System;
using System.Speech.Recognition;

namespace ImageSearch
{
    // Thin wrapper around the offline Windows speech recognizer (System.Speech / SAPI).
    // Free dictation is used so the user can say anything (a part name, a description)
    // and that text is then used as a keyword search over the image file names.
    public class VoiceSearchEngine : IDisposable
    {
        private SpeechRecognitionEngine engine;

        public event EventHandler<string> RecognizedText;
        public event EventHandler<string> StatusChanged;

        public bool IsAvailable { get; private set; }

        public VoiceSearchEngine()
        {
            IsAvailable = false;
            try
            {
                RecognizerInfo info = FindBestRecognizer();
                if (info == null)
                {
                    return;
                }

                engine = new SpeechRecognitionEngine(info);
                engine.LoadGrammar(new DictationGrammar());
                engine.SetInputToDefaultAudioDevice();
                engine.EndSilenceTimeout = TimeSpan.FromSeconds(1.5);
                engine.InitialSilenceTimeout = TimeSpan.FromSeconds(6);

                engine.SpeechRecognized += Engine_SpeechRecognized;
                engine.SpeechRecognitionRejected += Engine_SpeechRecognitionRejected;
                engine.RecognizeCompleted += Engine_RecognizeCompleted;
                engine.AudioStateChanged += Engine_AudioStateChanged;

                IsAvailable = true;
            }
            catch (Exception)
            {
                IsAvailable = false;
                engine = null;
            }
        }

        private static RecognizerInfo FindBestRecognizer()
        {
            RecognizerInfo fallback = null;
            foreach (RecognizerInfo ri in SpeechRecognitionEngine.InstalledRecognizers())
            {
                if (fallback == null)
                {
                    fallback = ri;
                }
                if (ri.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return ri;
                }
            }
            return fallback;
        }

        private void Engine_AudioStateChanged(object sender, AudioStateChangedEventArgs e)
        {
            if (e.AudioState == AudioState.Speech && StatusChanged != null)
            {
                StatusChanged(this, "正在聆聽...");
            }
        }

        private void Engine_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result != null && !string.IsNullOrEmpty(e.Result.Text) && RecognizedText != null)
            {
                RecognizedText(this, e.Result.Text);
            }
        }

        private void Engine_SpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            if (StatusChanged != null)
            {
                StatusChanged(this, "未辨識到語音內容，請再試一次");
            }
        }

        private void Engine_RecognizeCompleted(object sender, RecognizeCompletedEventArgs e)
        {
            if ((e.InitialSilenceTimeout || e.BabbleTimeout || e.Result == null) && StatusChanged != null)
            {
                StatusChanged(this, "沒有聽到語音輸入");
            }
        }

        public void ListenOnce()
        {
            if (!IsAvailable || engine == null)
            {
                return;
            }
            try
            {
                engine.RecognizeAsyncCancel();
            }
            catch (Exception)
            {
            }
            engine.RecognizeAsync(RecognizeMode.Single);
        }

        public void Stop()
        {
            if (engine == null)
            {
                return;
            }
            try
            {
                engine.RecognizeAsyncCancel();
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            if (engine != null)
            {
                engine.SpeechRecognized -= Engine_SpeechRecognized;
                engine.SpeechRecognitionRejected -= Engine_SpeechRecognitionRejected;
                engine.RecognizeCompleted -= Engine_RecognizeCompleted;
                engine.AudioStateChanged -= Engine_AudioStateChanged;
                engine.Dispose();
                engine = null;
            }
        }
    }
}
