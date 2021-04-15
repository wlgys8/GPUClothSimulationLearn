
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class ClothSimulation
{
    [System.Serializable]
    public class SimulateSetting{
        public Vector3 wind = new Vector3(0,0,10);
        public float windMultiplyAtNormal = 0f;
        public Vector3 springKs = new Vector3(25000,25000,25000);
        public float mass = 1;
        public float stepTime = 0.003f;
    }

    
    private static ComputeShader _cs;

    private static ComputeShader CS{
        get{
            if(!_cs){
                _cs = Resources.Load<ComputeShader>("ClothCS");
            }
            return _cs;
        }
    }

    private static Material _material;

    private static Material material{
        get{
            if(!_material){
                _material = new Material(Shader.Find("ClothSimulation/Unlit"));
            }
            return _material;
        }
    }


    private float _clothSize = 4;

    private SimulateSetting _simulateSetting = new SimulateSetting();

    private ComputeBuffer _positionBuffer;

    private ComputeBuffer _normalBuffer;

    private ComputeBuffer _velocitiesBuffer;

    private const int THREAD_X = 8;
    private const int THREAD_Y = 8;


    private int _totalVertexCount;
    private int _vertexCountPerDim = 32;

    private bool _initialized = false;

    private int _kernelInit;
    private int _kernelStepVelocity;
    private int _kernelStepPosition;

    private int _groupX;
    private int _groupY;

    public ClothSimulation(){
        _groupX = _vertexCountPerDim / THREAD_X;
        _groupY = _vertexCountPerDim / THREAD_Y;
    }

    public void UpdateSimulateSetting(SimulateSetting setting){
        _simulateSetting = setting;
        this.UpdateSimulateSetting();
    }
    
    public void UpdateSimulateSetting(){
        var viscousFluidArgs = (Vector4)_simulateSetting.wind;
        viscousFluidArgs.w = _simulateSetting.windMultiplyAtNormal;
        CS.SetVector("viscousFluidArgs",viscousFluidArgs);
        CS.SetVector("springKs",_simulateSetting.springKs);
        CS.SetFloat("mass",_simulateSetting.mass);
    }

    public void UpdateBallParams(Vector4 ball){
        CS.SetVector("collisionBall",ball);
    }
 
    public AsyncGPUReadbackRequest Initialize(){
        _kernelInit = CS.FindKernel("Init");
        _kernelStepVelocity = CS.FindKernel("StepV");
        _kernelStepPosition = CS.FindKernel("StepP");
     
        var vertexCount = _vertexCountPerDim;
        var totalVertex = vertexCount * vertexCount;
        var L0 = _clothSize / (vertexCount - 1);
        CS.SetInts("size",vertexCount,vertexCount,totalVertex);
        CS.SetVector("restLengths",new Vector3(L0,L0 * Mathf.Sqrt(2),L0 * 2));

        this.UpdateSimulateSetting();

        _positionBuffer = new ComputeBuffer(totalVertex,16);
        _velocitiesBuffer = new ComputeBuffer(totalVertex,16);
        _normalBuffer = new ComputeBuffer(totalVertex,16);

        System.Action<int> setBufferForKernet = (k)=>{
            CS.SetBuffer(k,"velocities",_velocitiesBuffer);
            CS.SetBuffer(k,"positions",_positionBuffer);
            CS.SetBuffer(k,"normals",_normalBuffer);
        };

        setBufferForKernet(_kernelInit);
        setBufferForKernet(_kernelStepVelocity);
        setBufferForKernet(_kernelStepPosition);

        CS.Dispatch(_kernelInit,_groupX,_groupY,1);

        _totalVertexCount = totalVertex;

        CreateIndexBuffer();
        material.SetBuffer(ShaderIDs.position, _positionBuffer );
        material.SetBuffer(ShaderIDs.normals,_normalBuffer);

        return AsyncGPUReadback.Request(_positionBuffer,(req)=>{
            if(req.hasError){
                Debug.LogError("Init error");
            }
            if(req.done && !req.hasError){
                _initialized = true;
            }
        });
    }

    GraphicsBuffer _indexBuffer;

	static class ShaderIDs {
		public static int position = Shader.PropertyToID( "_positions" );
        public static int normals = Shader.PropertyToID( "_normals" );
	}
    private void CreateIndexBuffer(){
        var vertexCount = _vertexCountPerDim;
        var quadCount = (vertexCount - 1) * (vertexCount - 1);
        _indexBuffer = new GraphicsBuffer( GraphicsBuffer.Target.Index, quadCount * 6, sizeof( int ) );
        int[] indicies = new int[_indexBuffer.count];
        for(var x = 0; x < vertexCount - 1; x ++){
            for(var y = 0; y < vertexCount - 1; y ++){
                var vertexIndex = (y * vertexCount + x);
                var quadIndex = y * (vertexCount - 1) + x;
                var upVertexIndex = (vertexIndex + vertexCount);
                var offset = quadIndex * 6;
                indicies[offset ] = vertexIndex; 
                indicies[offset + 1] =  (vertexIndex + 1); 
                indicies[offset + 2] =  upVertexIndex; 

                indicies[offset + 3] = upVertexIndex; 
                indicies[offset + 4] =  (vertexIndex + 1); 
                indicies[offset + 5] =  (upVertexIndex + 1); 
            }
        }
        _indexBuffer.SetData(new List<int>(indicies));
    }


    public IEnumerator StartAsync(){
        yield return Initialize();
        float dt = 0;
        float minDt = _simulateSetting.stepTime;
        while(true){
            dt += Time.deltaTime;
            while(dt > minDt){
                CS.SetFloat("deltaTime",minDt);
                CS.Dispatch(_kernelStepVelocity,_groupX,_groupY,1);
                CS.Dispatch(_kernelStepPosition,_groupX,_groupY,1);
                dt -= minDt;
            }
            yield return null;
            AsyncGPUReadback.WaitAllRequests();
        }
    }

    public void Draw(){
        if(!_initialized){
            return;
        }
        material.SetPass(0);
        Graphics.DrawProceduralNow(MeshTopology.Triangles,_indexBuffer,_indexBuffer.count,1);
    }

    public void Dispose(){
        Debug.Log("release buffers");
        if(_positionBuffer != null){
            _positionBuffer.Release();
            _positionBuffer = null;
        }
        if(_velocitiesBuffer != null){
            _velocitiesBuffer.Release();
            _velocitiesBuffer = null;
        }
        if(_indexBuffer != null){
            _indexBuffer.Release();
            _indexBuffer = null;
        }
    }
}
