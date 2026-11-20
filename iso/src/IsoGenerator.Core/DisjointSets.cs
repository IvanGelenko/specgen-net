namespace IsoGenerator.Core;

internal sealed class DisjointSets
{
    readonly List<int> parent=new();
    public int Add(){int i=parent.Count;parent.Add(i);return i;}
    public int Find(int i){int root=i;while(parent[root]!=root)root=parent[root];while(parent[i]!=i){int next=parent[i];parent[i]=root;i=next;}return root;}
    public void Join(int a,int b){a=Find(a);b=Find(b);if(a!=b)parent[b]=a;}
}
