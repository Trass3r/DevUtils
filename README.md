DevUtils
========

This Visual Studio extension adds some convenience features I missed for C++ and C# development.

- adds editor context menu items for inspecting the C++ preprocessor and code generator output resp. C# disassembly via ILSpy
- taskbar progress visualization and total solution build time

In conjunction with an assembler syntax highlighting extension like AsmHighlighter this provides an easy way to optimize code or figure out preprocessor issues.

It strives to jump to the correct place in the generated output file if possible.
This may not be correct if you are in a template function and want to see the assembly or the cursor is placed on a line using macros and preprocessed output is desired.


Download
--------
https://marketplace.visualstudio.com/vsgallery/62f485b0-b659-4852-8f39-885c20c9fcd1


TODO
----
- better methods for finding the correct spot in the generated file
- RunToCursor functionality with ignoring breakpoints


    Dim bptStates(DTE.Debugger.Breakpoints.Count - 1) As Boolean


    Dim i = 0
    For Each bpt As Breakpoint In DTE.Debugger.Breakpoints
        bptStates(i) = bpt.Enabled
        i += 1
        bpt.Enabled = False
    Next

    Try
        DTE.Debugger.RunToCursor(True)
        '       Catch ex As Exception
    Finally
        i = 0
        For Each bpt As Breakpoint In DTE.Debugger.Breakpoints
            bpt.Enabled = bptStates(i)
            i += 1
        Next
    End Try
	
	
	But for some reason the RunToCursor call doesn't work at all ("Operation not supported") and breakpoint states aren't properly reset either. Some are reactivated but not all. Any ideas?