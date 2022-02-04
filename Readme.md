# BetterCecil

Better Cecil is a collection of helper methods for using Mono.Cecil. 

Better Cecil is created to be used in unity so comes with classes to be used with unity's ILPostProcessing

## Requires and Install
- Mono.cecil

Add `"com.unity.nuget.mono-cecil": "1.10.2",` to your `manifest.json` and unity will important the dependencies.

## Install 

Import all files into project

## How to use 

```cs
public class MyILPostProcessor : ILPostProcessor
{
    public override ILPostProcessor GetInstance() => this;

    public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
    {
        // call WillProcess because unity doesn't check it before calling Process
        if (!WillProcess(compiledAssembly))
            return null;

        // Use inherited class WeaverBase to return ILPostProcessResult
        var weaver = new MyWeaver();
        return weaver.Process(compiledAssembly);
    }

    public override bool WillProcess(ICompiledAssembly compiledAssembly) 
    { 
        // here to only process some assemblies
        return true;
    }
}

public class MyWeaver : WeaverBase
{
    protected override Result Process(AssemblyDefinition assembly) 
    {
        // put your server code here 

        // Example:
        // check all types in assembly
        ModuleDefinition module = assembly.MainModule;
        foreach (TypeDefinition type in module.Types)
        {
            // do stuff with types here
        }
    }
}
```


## MIT License

Some of the extension methods in here are originally from [Mirage](https://github.com/MirageNet/Mirage) and [Mirror](https://github.com/vis2k/Mirror)

The original ReflectionImporter and AssemblyResolver code is from [com.unity.netcode.gameobjects](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/tree/develop/com.unity.netcode.gameobjects/Editor/CodeGen)