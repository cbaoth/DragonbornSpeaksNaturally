#pragma once
#include "common/IPrefix.h"
#include <cstdlib>
#include <string>
#include <fstream>

class Log
{
	static Log* instance;

	bool logEnabled = true;
	std::ofstream logFile;

	Log();
	~Log();
	void writeLine(std::string message);

public:
	static Log* get();

	static void info(std::string message);
	static void address(std::string message, uintptr_t addr);
	static void hex(std::string message, uintptr_t addr);
};