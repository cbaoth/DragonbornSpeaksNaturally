#include "Log.h"
#include <sstream>
#include <fstream>
#include <windows.h>
#include <ShlObj.h>

Log* Log::instance = NULL;

Log::Log()
{
	std::string baseDir = "";

	{
		CHAR myDocuments[MAX_PATH];
		HRESULT result = SHGetFolderPathA(NULL, CSIDL_MYDOCUMENTS, NULL, SHGFP_TYPE_CURRENT, myDocuments);

		if (result == S_OK) {
			baseDir = std::string(myDocuments) + "/DragonbornSpeaksNaturally";
			// create dir
			SHCreateDirectoryEx(NULL, baseDir.c_str(), NULL);
			baseDir += "/";
		}
	}

	logFile.open(baseDir + "dragonborn_speaks.log", std::ios_base::out | std::ios_base::app);
	if (!logFile) {
		logEnabled = false;
	}
}

Log::~Log()
{
	logFile.close();
}

void Log::writeLine(std::string message)
{
	if (!logEnabled) {
		return;
	}
	logFile << message << std::endl;
	logFile.flush();
}

Log* Log::get() {
	if (!instance)
		instance = new Log();
	return instance;
}

void Log::info(std::string message) {
	get()->writeLine(message);
}

void Log::address(std::string message, uintptr_t addr) {
	std::stringstream ss;
	ss << std::hex << addr;
	const std::string s = ss.str();
	Log::info(message.append(s));
}

void Log::hex(std::string message, uintptr_t addr) {
	std::stringstream ss;
	ss << std::hex << addr;
	const std::string s = ss.str();
	Log::info(message.append(s));
}