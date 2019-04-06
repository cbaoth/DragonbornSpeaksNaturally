#include "Hooks.h"
#include <string.h>
#include "Log.h"
#include "common/IPrefix.h"
#include "skse64_common/SafeWrite.h"
#include "skse64/ScaleformAPI.h"
#include "skse64/ScaleformMovie.h"
#include "skse64/ScaleformValue.h"
#include "skse64/GameInput.h"
#include "skse64_common/BranchTrampoline.h"
#include "xbyak.h"
#include "SkyrimType.h"
#include "ConsoleCommandRunner.h"
#include "FavoritesMenuManager.h"

class RunCommandSink;

static RunCommandSink *runCommandSink = NULL;
static GFxMovieView* dialogueMenu = NULL;
static int desiredTopicIndex = 1;
static int numTopics = 0;
static int lastMenuState = -1;
typedef UInt32 getDefaultCompiler(void* unk01, char* compilerName, UInt32 unk03);
typedef void executeCommand(UInt32* unk01, void* parser, char* command);

static void __cdecl Hook_Loop()
{
	if (dialogueMenu != NULL)
	{
		// Menu exiting, avoid NPE
		if (dialogueMenu->GetPause() == 0)
		{
			dialogueMenu = NULL;
			SpeechRecognitionClient::getInstance()->StopDialogue();
			return;
		}
		GFxValue stateVal;
		dialogueMenu->GetVariable(&stateVal, "_level0.DialogueMenu_mc.eMenuState");
		int menuState = stateVal.data.number;
		desiredTopicIndex = SpeechRecognitionClient::getInstance()->ReadSelectedIndex();
		if (menuState != lastMenuState) {

			lastMenuState = menuState;
			if (menuState == 2) // NPC Responding
			{
				// fix issue #11 (SSE crash when teleport with a dialogue line).
				// It seems no side effects have been found at present.
				dialogueMenu = NULL;
				SpeechRecognitionClient::getInstance()->StopDialogue();
				return;
			}
		}
		if (desiredTopicIndex >= 0) {
			GFxValue topicIndexVal;
			dialogueMenu->GetVariable(&topicIndexVal, "_level0.DialogueMenu_mc.TopicList.iSelectedIndex");

			int currentTopicIndex = topicIndexVal.data.number;
			if (currentTopicIndex != desiredTopicIndex) {

				dialogueMenu->Invoke("_level0.DialogueMenu_mc.TopicList.SetSelectedTopic", NULL, "%d", desiredTopicIndex);
				dialogueMenu->Invoke("_level0.DialogueMenu_mc.TopicList.doSetSelectedIndex", NULL, "%d", desiredTopicIndex);
				dialogueMenu->Invoke("_level0.DialogueMenu_mc.TopicList.UpdateList", NULL, NULL, 0);
			}

			dialogueMenu->Invoke("_level0.DialogueMenu_mc.onSelectionClick", NULL, "%d", 1.0);
		}
		else if (desiredTopicIndex == -2) { // Indicates a "goodbye" phrase was spoken, hide the menu
			dialogueMenu->Invoke("_level0.DialogueMenu_mc.StartHideMenu", NULL, NULL, 0);
		}
	}
	else
	{
		std::string command = SpeechRecognitionClient::getInstance()->PopCommand();
		if (command != "") {
			ConsoleCommandRunner::RunCommand(command);
			Log::info("run command: " + command);
		}

		if (g_SkyrimType == VR) {
			FavoritesMenuManager::getInstance()->ProcessEquipCommands();
		}
	}
}

class RunCommandSink : public BSTEventSink<InputEvent> {
	EventResult ReceiveEvent(InputEvent ** evnArr, InputEventDispatcher * dispatcher) override {
		Hook_Loop();
		return kEvent_Continue;
	}
};

static void __cdecl Hook_Invoke(GFxMovieView* movie, char * gfxMethod, GFxValue* argv, UInt32 argc)
{
	static bool inited = false;
	if (!inited) {
		runCommandSink = new RunCommandSink;
		// Currently in the source code directory is the latest version of SKSE64 instead of SKSEVR,
		// so we can call GetSingleton() directly instead of use a RelocAddr.
		auto inputEventDispatcher = InputEventDispatcher::GetSingleton();
		inputEventDispatcher->AddEventSink(runCommandSink);
		inited = true;
		Log::info("RunCommandSink Initialized");
	}

	if (argc >= 1)
	{
		GFxValue commandVal = argv[0];
		if (commandVal.type == 4) { // Command
			const char* command = commandVal.data.string;
			//Log::info(command); // TEMP
			if (strcmp(command, "PopulateDialogueList") == 0)
			{
				numTopics = (argc - 2) / 3;
				desiredTopicIndex = -1;
				dialogueMenu = movie;
				std::vector<std::string> lines;
				for (int j = 1; j < argc - 1; j = j + 3)
				{
					GFxValue dialogueLine = argv[j];
					const char* dialogueLineStr = dialogueLine.data.string;
					lines.push_back(std::string(dialogueLineStr));
				}

				DialogueList dialogueList;
				dialogueList.lines = lines;
				SpeechRecognitionClient::getInstance()->StartDialogue(dialogueList);
			}
			else if (g_SkyrimType == VR && strcmp(command, "UpdatePlayerInfo") == 0)
			{
				FavoritesMenuManager::getInstance()->RefreshFavorites();
			}
		}
	}
}

static void __cdecl Hook_PostLoad() {
	if (g_SkyrimType == VR) {
		FavoritesMenuManager::getInstance()->RefreshFavorites();
	}
}

static uintptr_t invokeTarget = 0x0;
static uintptr_t invokeReturn = 0x0;
static uintptr_t loadEventEnter = 0x0;
static uintptr_t loadEventTarget = 0x0;

void Hooks_Inject(void)
{
	uintptr_t kHook_Invoke_Enter = InvokeFunction.GetUIntPtr() + 0xEE;

	// x64 "call" instruction: E8 <32-bit target offset>
	uint32_t *pInvokeTargetOffset = (uint32_t *)(kHook_Invoke_Enter + 1);

	// <call target address> = <call instruction beginning address> + <call instruction's size (5 bytes)> + <32-bit target offset>
	uintptr_t kHook_Invoke_Target = kHook_Invoke_Enter + 5 + *pInvokeTargetOffset;

	uintptr_t kHook_Invoke_Return = kHook_Invoke_Enter + 0x14;

	invokeTarget = kHook_Invoke_Target;
	invokeReturn = kHook_Invoke_Return;

	Log::address("Invoke Enter: ", kHook_Invoke_Enter);
	Log::address("Invoke Target: ", kHook_Invoke_Target);

	/***
	Post Load HOOK - VR Only
	**/
	if (g_SkyrimType == VR) {
		// "Finished loading game" print statement, initialize player orientation?
		RelocAddr<uintptr_t> kHook_LoadEvent_Enter(0x5852A4);

		// Initialize player orientation target addr
		RelocAddr<uintptr_t> kHook_LoadEvent_Target(0x6AB5E0);

		loadEventEnter = kHook_LoadEvent_Enter;
		loadEventTarget = kHook_LoadEvent_Target;

		Log::address("LoadEvent Enter: ", kHook_LoadEvent_Enter);
		Log::address("LoadEvent Target: ", kHook_LoadEvent_Target);

		struct Hook_LoadEvent_Code : Xbyak::CodeGenerator {
			Hook_LoadEvent_Code(void * buf) : Xbyak::CodeGenerator(4096, buf)
			{
				// Invoke original virtual method
				mov(rax, (uintptr_t)loadEventTarget);
				call(rax);

				// Call our method
				sub(rsp, 0x30);
				mov(rax, (uintptr_t)Hook_PostLoad);
				call(rax);
				add(rsp, 0x30);

				// Return 
				mov(rax, loadEventEnter + 0x5);
				jmp(rax);
			}
		};
		void * codeBuf = g_localTrampoline.StartAlloc();
		Hook_LoadEvent_Code loadEventCode(codeBuf);
		g_localTrampoline.EndAlloc(loadEventCode.getCurr());
		g_branchTrampoline.Write5Branch(kHook_LoadEvent_Enter, uintptr_t(loadEventCode.getCode()));
	}

	/***
	Invoke "call" HOOK
	**/
	{
		struct Hook_Entry_Code : Xbyak::CodeGenerator {
			Hook_Entry_Code(void * buf) : Xbyak::CodeGenerator(4096, buf)
			{
				push(rcx);
				push(rdx);
				push(r8);
				push(r9);
				sub(rsp, 0x30);
				mov(rax, (uintptr_t)Hook_Invoke);
				call(rax);
				add(rsp, 0x30);
				pop(r9);
				pop(r8);
				pop(rdx);
				pop(rcx);

				mov(rax, invokeTarget);
				call(rax);

				mov(rbx, ptr[rsp + 0x50]);
				mov(rsi, ptr[rsp + 0x60]);
				add(rsp, 0x40);
				pop(rdi);

				mov(rax, invokeReturn);
				jmp(rax);
			}
		};

		void * codeBuf = g_localTrampoline.StartAlloc();
		Hook_Entry_Code entryCode(codeBuf);
		g_localTrampoline.EndAlloc(entryCode.getCurr());
		g_branchTrampoline.Write5Branch(kHook_Invoke_Enter, uintptr_t(entryCode.getCode()));
	}
}
