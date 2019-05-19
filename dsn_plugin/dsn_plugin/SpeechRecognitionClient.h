#pragma once
#include "common/IPrefix.h"

#include <vector>
#include <queue>
#include <string>
#include <windows.h>
#include <sstream>
#include <mutex>

struct DialogueList
{
	std::vector<std::string> lines;

	DialogueList() {
	}

	DialogueList(const DialogueList &r) {
		lines = r.lines;
	}

	void clear() {
		lines.clear();
	}

	bool operator==(const DialogueList &r) {
		return r.lines == lines;
	}
};

class SpeechRecognitionClient
{
public:
	static SpeechRecognitionClient* getInstance();
	static void Initialize();

	~SpeechRecognitionClient();

	void SetHandles(HANDLE h_stdInWr, HANDLE h_stdOutRd) {
		stdInWr = h_stdInWr;
		stdOutRd = h_stdOutRd;
	}

	void StopDialogue();
	void StartDialogue(DialogueList list);

	void WriteLine(std::string str);
	int ReadSelectedIndex();

	std::string PopCommand();
	std::string PopEquip();
	void EnqueueCommand(std::string command);
	void EnqueueEquip(std::string equip);

	void AwaitResponses();

private:
	SpeechRecognitionClient();
	std::string ReadLine();

	static SpeechRecognitionClient* instance;

	HANDLE stdInWr;
	HANDLE stdOutRd;
	int selectedIndex = -1;
	int currentDialogueId = 0;
	DialogueList currentDialogueList;
	std::string workingLine;
	std::mutex queueLock;
	std::queue<std::string> queuedCommands;
	std::queue<std::string> queuedEquips;
};
