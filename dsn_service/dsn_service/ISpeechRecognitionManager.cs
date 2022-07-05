using System.Speech.Recognition;

namespace DSN
{
    delegate void DialogueLineRecognitionHandler(string text, Grammar grammar, string semantics);

    interface ISpeechRecognitionManager
    {
        void StartSpeechRecognition(bool isDialogueMode, params ISpeechRecognitionGrammarProvider[] grammarProviders);
        void StopRecognition();
        void Stop();

        event DialogueLineRecognitionHandler OnDialogueLineRecognized;
    }
}
