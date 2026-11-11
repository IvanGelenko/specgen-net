namespace IsoGenerator.Core;
// Jointly adjust paper pipe lengths; rigid symbols and true model coordinates stay unchanged.
internal sealed class CycleLengthSolver
{
    readonly List<double[]> cycles=new();readonly int[] variables;readonly SheetPoint[] directions;
    public bool HasCycles=>cycles.Count>0;
    public int Count=>cycles.Count/2;
    public CycleLengthSolver(DimensionChainPlanner.Graph graph,int turns)
    {
        int count=graph.Edges.Count;directions=new SheetPoint[count];variables=Enumerable.Range(0,count).Where(i=>graph.Edges[i].Kind=="Pipe").ToArray();
        var parent=new Dictionary<int,int>();var tree=new Dictionary<int,List<(int Node,int Edge)>>();
        int Find(int n){if(!parent.ContainsKey(n))parent[n]=n;int root=n;while(parent[root]!=root)root=parent[root];while(parent[n]!=n){int next=parent[n];parent[n]=root;n=next;}return root;}
        for(int i=0;i<count;i++)
        {
            var edge=graph.Edges[i];int a=graph.Sets.Find(edge.A),b=graph.Sets.Find(edge.B);
            if(a==b)continue; // A topology-collapsed edge is not an independent cycle.
            var p=IsometricProjection.Project(graph.Nodes[a].Point,quarterTurns:turns);var q=IsometricProjection.Project(graph.Nodes[b].Point,quarterTurns:turns);
            double dx=q.X-p.X,dy=q.Y-p.Y,length=Math.Sqrt(dx*dx+dy*dy);
            if(length<.001)throw new InvalidDataException($"{edge.Source}: участок вырождается в выбранном ракурсе; выберите другое направление севера.");
            directions[i]=new(dx/length,dy/length);
            if(Find(a)!=Find(b))
            {parent[Find(a)]=Find(b);if(!tree.ContainsKey(a))tree[a]=new();if(!tree.ContainsKey(b))tree[b]=new();tree[a].Add((b,i));tree[b].Add((a,i));continue;}
            var previous=new Dictionary<int,(int Node,int Edge)>{{a,(-1,-1)}};var queue=new Queue<int>();queue.Enqueue(a);
            while(queue.Count>0&&!previous.ContainsKey(b)){int node=queue.Dequeue();foreach(var next in tree[node])if(previous.TryAdd(next.Node,(node,next.Edge)))queue.Enqueue(next.Node);}
            var x=new double[count];var y=new double[count];x[i]=directions[i].X;y[i]=directions[i].Y;
            for(int node=b;node!=a;)
            {var step=previous[node];double sign=graph.Sets.Find(graph.Edges[step.Edge].A)==step.Node?1:-1;x[step.Edge]-=sign*directions[step.Edge].X;y[step.Edge]-=sign*directions[step.Edge].Y;node=step.Node;}
            cycles.Add(x);cycles.Add(y);
        }
    }
    public double[] Constrain(double[] preferred,double minimum,IReadOnlyList<double>? lowerBounds=null)
    {
        if(!HasCycles)return preferred;
        int m=cycles.Count,n=variables.Length;var variableSet=variables.ToHashSet();var weights=variables.Select(i=>Math.Max(lowerBounds?[i]??minimum,preferred[i])).ToArray();
        var a=new double[m,n];var b=new double[m];
        for(int row=0;row<m;row++)
        {for(int edge=0;edge<preferred.Length;edge++)if(!variableSet.Contains(edge))b[row]-=cycles[row][edge]*preferred[edge];for(int column=0;column<n;column++)a[row,column]=cycles[row][variables[column]]*weights[column];}
        var gram=new double[m,m];var transform=new double[m,m];double largest=0;
        for(int i=0;i<m;i++)for(int j=0;j<m;j++){for(int k=0;k<n;k++)gram[i,j]+=a[i,k]*a[j,k];largest=Math.Max(largest,Math.Abs(gram[i,j]));transform[i,j]=i==j?1:0;}
        var pivots=new List<int>();int rank=0;
        for(int column=0;column<m;column++)
        {
            int pivot=rank;for(int r=rank;r<m;r++)if(Math.Abs(gram[r,column])>Math.Abs(gram[pivot,column]))pivot=r;
            if(Math.Abs(gram[pivot,column])<=Math.Max(1e-12,largest*1e-12))continue;
            for(int j=0;j<m;j++){(gram[pivot,j],gram[rank,j])=(gram[rank,j],gram[pivot,j]);(transform[pivot,j],transform[rank,j])=(transform[rank,j],transform[pivot,j]);}
            double divisor=gram[rank,column];for(int j=0;j<m;j++){gram[rank,j]/=divisor;transform[rank,j]/=divisor;}
            for(int r=0;r<m;r++)if(r!=rank){double factor=gram[r,column];for(int j=0;j<m;j++){gram[r,j]-=factor*gram[rank,j];transform[r,j]-=factor*transform[rank,j];}}
            pivots.Add(column);rank++;if(rank==m)break;
        }
        double[] Multipliers(double[] rhs)
        {
            var reduced=new double[m];for(int i=0;i<m;i++)for(int j=0;j<m;j++)reduced[i]+=transform[i,j]*rhs[j];
            for(int i=rank;i<m;i++)if(Math.Abs(reduced[i])>1e-5)throw new InvalidDataException("Контур не удаётся замкнуть изменением длин труб: проверьте направления портов жёстких элементов.");
            var result=new double[m];for(int i=0;i<rank;i++)result[pivots[i]]=reduced[i];return result;
        }
        var value=variables.Select((edge,i)=>preferred[edge]/weights[i]).ToArray();var affine=new double[n];var bounds=new double[n];double error=double.MaxValue;
        // Dykstra projection onto the affine closure space and positive pipe-length bounds.
        for(int iteration=0;iteration<6000;iteration++)
        {
            if(iteration%32==0)PlanningExecution.Check();
            if(iteration%128==0)PlanningExecution.Report("Замыкание контуров");
            var trial=new double[n];for(int i=0;i<n;i++)trial[i]=value[i]+affine[i];
            var rhs=new double[m];for(int r=0;r<m;r++){rhs[r]=-b[r];for(int i=0;i<n;i++)rhs[r]+=a[r,i]*trial[i];}
            var lambda=Multipliers(rhs);var next=new double[n];
            for(int i=0;i<n;i++){double projected=trial[i];for(int r=0;r<m;r++)projected-=a[r,i]*lambda[r];affine[i]=trial[i]-projected;double candidate=projected+bounds[i];next[i]=Math.Max((lowerBounds?[variables[i]]??minimum)/weights[i],candidate);bounds[i]=candidate-next[i];}
            value=next;error=0;for(int r=0;r<m;r++){double residual=-b[r];for(int i=0;i<n;i++)residual+=a[r,i]*value[i];error=Math.Max(error,Math.Abs(residual));}
            if(error<1e-6)break;
        }
        if(error>.01)throw new SheetCapacityException($"Не удалось совместить замкнутые контуры с минимальной длиной труб; невязка {error:F3} мм.");
        var lengths=preferred.ToArray();for(int i=0;i<n;i++)lengths[variables[i]]=value[i]*weights[i];return lengths;
    }
}
