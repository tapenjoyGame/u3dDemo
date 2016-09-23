

using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class jzApplication : MonoBehaviour
{
    public static jzApplication app;

    void Awake()
    {
        Application.targetFrameRate = 60;
        DontDestroyOnLoad(this.gameObject);
        app = this;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    // Use this for initialization
    void Start ()
    {
         this.loadSceneAsync("login");

//         Debug.Log("[Start] start" + Time.time);
//         StartCoroutine(coroutineHandler(2));
//         Debug.Log("[Start] end" + Time.time);
    }
	
	// Update is called once per frame
	void Update ()
    {
	
	}

    //切换场景
    void loadScene(string scene)
    {
        SceneManager.LoadScene(scene);
    }

    void loadSceneAsync(string scene)
    {
        StartCoroutine(_loadSceneAsyncHandler(scene));
    }

    IEnumerator _loadSceneAsyncHandler(string scene)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        while (!op.isDone)
        {
            Debug.Log(op.progress * 100);

            yield return null;
        }

        Debug.Log("[_loadSceneAsyncHandler] end.");
    }

    IEnumerator coroutineHandler(float time)
    {
        Debug.Log("[coroutineHandler] start" + Time.time);
        yield return new WaitForSeconds(time);
        Debug.Log("[coroutineHandler] end" + Time.time);
    }
}
