DevUtils
========

This Visual Studio extension adds some convenience features I missed in particular for C++ development.

- adds editor context menu items for inspecting the preprocessor and code generator output
- taskbar progress visualization and total solution build time

In conjunction with an assembler syntax highlighting extension like AsmHighlighter this provides an easy way to optimize code or figure out preprocessor issues.

It strives to jump to the correct place in the generated output file if possible.
This may not be correct if you are in a template function and want to see the assembly or the cursor is placed on a line using macros and preprocessed output is desired.


Download
--------
https://visualstudiogallery.msdn.microsoft.com/62f485b0-b659-4852-8f39-885c20c9fcd1

Known issues
------------

- Doesn't work with headers.
- It does undo the changes to the compile options afterwards but the project file will still have a new empty entry for that file added if you save it.


TODO
----
- cleanup!
- better methods for finding the correct spot in the generated file
- prevent pollution of project file by finding a working undo mechanism
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