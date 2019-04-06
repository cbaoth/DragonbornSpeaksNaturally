#pragma once
#include "common/IPrefix.h"
#include "skse64/GameMenus.h"

class DSNMenuManager // Copy of SKSE MenuManager, but with create menu method
{
public:
	static IMenu * GetOrCreateMenu(const char * menuName);
};