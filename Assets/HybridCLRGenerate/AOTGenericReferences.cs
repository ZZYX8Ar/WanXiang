using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"WanXiang.Runtime.dll",
	};
	// }}

	// {{ constraint implement type
	// }} 

	// {{ AOT generic types
	// WanXiang.Framework.HotUpdate.AOTMetadataProbe.ProbeBox<int>
	// }}

	public void RefMethods()
	{
		// WanXiang.Framework.HotUpdate.AOTMetadataProbe.ProbeBox<int> WanXiang.Framework.HotUpdate.AOTMetadataProbe.Box<int>(int)
		// string WanXiang.Framework.HotUpdate.AOTMetadataProbe.Describe<int>(int)
		// string WanXiang.Framework.HotUpdate.AOTMetadataProbe.DescribePair<object,long>(object,long)
	}
}