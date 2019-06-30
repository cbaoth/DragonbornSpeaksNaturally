#include "SkyrimType.h"

SkyrimType g_SkyrimType(SE);

UInt64 SKYRIM_VERSION[2] = {
	0x0001000500500000,  // SkyrimSE
	0x00010004000F0000   // SkyrimVR
};

std::string SKYRIM_VERSION_STR[2] = {
	"1.5.80.0",
	"1.4.15.0"
};
