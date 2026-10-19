using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

// Public UIAutomationClient.h interfaces, SDK 10.0.26100. Unused slots are
// retained in SDK order and never called. All objects stay on one MTA worker.
internal static class SystemControlUiaInterop
{
    [ComImport, Guid("34723aff-0c9d-49d0-9896-7ab52df8cd8a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAutomation
    {
        void CompareElements(); // Unused SDK slot.
        void CompareRuntimeIds(); // Unused SDK slot.
        void GetRootElement(); // Unused SDK slot.
        void ElementFromHandle(nint hwnd, out IElement element);
        void ElementFromPoint(); // Unused SDK slot.
        void GetFocusedElement(); // Unused SDK slot.
        void GetRootElementBuildCache(); // Unused SDK slot.
        void ElementFromHandleBuildCache(); // Unused SDK slot.
        void ElementFromPointBuildCache(); // Unused SDK slot.
        void GetFocusedElementBuildCache(); // Unused SDK slot.
        void CreateTreeWalker(); // Unused SDK slot.
        void get_ControlViewWalker(); // Unused SDK slot.
        void get_ContentViewWalker(); // Unused SDK slot.
        void get_RawViewWalker(); // Unused SDK slot.
        void get_RawViewCondition(); // Unused SDK slot.
        void get_ControlViewCondition(); // Unused SDK slot.
        void get_ContentViewCondition(); // Unused SDK slot.
        void CreateCacheRequest(); // Unused SDK slot.
        void CreateTrueCondition(out ICondition condition);
        void CreateFalseCondition(); // Unused SDK slot.
        void CreatePropertyCondition(int property, [MarshalAs(UnmanagedType.Struct)] object value, out ICondition condition);
        void CreatePropertyConditionEx(); // Unused SDK slot.
        void CreateAndCondition(); // Unused SDK slot.
        void CreateAndConditionFromArray(); // Unused SDK slot.
        void CreateAndConditionFromNativeArray(); // Unused SDK slot.
        void CreateOrCondition(ICondition first, ICondition second, out ICondition condition);
        void CreateOrConditionFromArray(); // Unused SDK slot.
        void CreateOrConditionFromNativeArray(); // Unused SDK slot.
        void CreateNotCondition(); // Unused SDK slot.
        void AddAutomationEventHandler(); // Unused SDK slot.
        void RemoveAutomationEventHandler(); // Unused SDK slot.
        void AddPropertyChangedEventHandlerNativeArray(); // Unused SDK slot.
        void AddPropertyChangedEventHandler(); // Unused SDK slot.
        void RemovePropertyChangedEventHandler(); // Unused SDK slot.
        void AddStructureChangedEventHandler(); // Unused SDK slot.
        void RemoveStructureChangedEventHandler(); // Unused SDK slot.
        void AddFocusChangedEventHandler(); // Unused SDK slot.
        void RemoveFocusChangedEventHandler(); // Unused SDK slot.
        void RemoveAllEventHandlers(); // Unused SDK slot.
        void IntNativeArrayToSafeArray(); // Unused SDK slot.
        void IntSafeArrayToNativeArray(); // Unused SDK slot.
        void RectToVariant(); // Unused SDK slot.
        void VariantToRect(); // Unused SDK slot.
        void SafeArrayToRectNativeArray(); // Unused SDK slot.
        void CreateProxyFactoryEntry(); // Unused SDK slot.
        void get_ProxyFactoryMapping(); // Unused SDK slot.
        void GetPropertyProgrammaticName(); // Unused SDK slot.
        void GetPatternProgrammaticName(); // Unused SDK slot.
        void PollForPotentialSupportedPatterns(); // Unused SDK slot.
        void PollForPotentialSupportedProperties(); // Unused SDK slot.
        void CheckNotSupported(); // Unused SDK slot.
        void get_ReservedNotSupportedValue(); // Unused SDK slot.
        void get_ReservedMixedAttributeValue(); // Unused SDK slot.
        void ElementFromIAccessible(); // Unused SDK slot.
        void ElementFromIAccessibleBuildCache(); // Unused SDK slot.
        void get_AutoSetFocus(out int value);
        void put_AutoSetFocus(int value);
        void get_ConnectionTimeout(out uint value);
        void put_ConnectionTimeout(uint value);
        void get_TransactionTimeout(out uint value);
        void put_TransactionTimeout(uint value);
    }
    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICondition { }
    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll(int scope, ICondition condition, out IElements found);
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        void GetCurrentPropertyValue(int property, [MarshalAs(UnmanagedType.Struct)] out object value);
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        void GetCurrentPattern(int pattern, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }
    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IElements
    {
        void get_Length(out int length);
        void GetElement(int index, out IElement element);
    }
    [ComImport, Guid("94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IToggle
    {
        void Toggle();
        void get_CurrentToggleState(out int state);
        void get_CachedToggleState(out int state);
    }
    [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInvoke { void Invoke(); }
    [ComImport, Guid("b488300f-d015-4f19-9c29-bb595e3645ef"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IScrollItem { void ScrollIntoView(); }
}
