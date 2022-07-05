using System.Speech.Recognition;

namespace DSN
{
    delegate void DialogueLineRecognitionHandler(RecognitionResult result);

    interface ISpeechRecognitionManager
    {
        void StartSpeechRecognition(bool isDialogueMode, params ISpeechRecognitionGrammarProvider[] grammarProviders);
        void StopRecognition();
        void Stop();

        event DialogueLineRecognitionHandler OnDialogueLineRecognized;
    }
}
