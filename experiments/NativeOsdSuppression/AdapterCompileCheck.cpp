#include <windows.h>
extern int Wh_GetIntSetting(PCWSTR);
extern BOOL Wh_SetFunctionHook(void*, void*, void**);
extern void Wh_Log(PCWSTR, ...);
extern size_t Wh_GetModStoragePath(PWSTR, size_t);
#include "explorer-prototype.wh.cpp"
