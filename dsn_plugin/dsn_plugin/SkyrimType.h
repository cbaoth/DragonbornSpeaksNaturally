#pragma once
#ifndef _SKYRIM_TYPE_H_
#define _SKYRIM_TYPE_H_

#include "common/IPrefix.h"
#include <string>

enum SkyrimType { SE = 0, VR = 1 };

extern SkyrimType g_SkyrimType;

extern UInt64 SKYRIM_VERSION[2];

extern std::string SKYRIM_VERSION_STR[2];

#endif
