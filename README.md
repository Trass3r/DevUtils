DevUtils
========

For simple tasks like checking the output of the C++ preprocessor or the code generation Visual Studio requires you to:
- go to the file or project properties and set the correct compiler flags and output options
- run the compiler
- locate the generated file
- find your function in the hundreds of thousands of LoCs

This is an extension that automates those tedious steps via convenient context menu entries.

It is alpha quality but does its job.
Should be used together with an assembler syntax highlighting extension like AsmHighlighter.


Known issues
------------

- Doesn't work with headers.
- Currently uses Release|x64 hard-coded.
- It does undo the changes to the compile options afterwards but the project file will still have a new empty entry for that file added if you save it.
- https://connect.microsoft.com/VisualStudio/feedback/details/809115/vcfileconfiguration-compile-only-works-with-the-currently-active-configuration