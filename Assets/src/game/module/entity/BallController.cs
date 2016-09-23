using UnityEngine;
using System.Collections;

public class BallController : MonoBehaviour
{

    protected GameObject owner;
    protected Rigidbody rb;

	// Use this for initialization
	void Start ()
	{
	    this.owner = this.gameObject;
        this.rb = this.gameObject.GetComponent<Rigidbody>();
        this.rb = this.GetComponent<Rigidbody>();
	    Debug.Assert(this.rb != null);
	}
	
	// Update is called once per frame
	void Update ()
    {
        this.rb.AddForce(new Vector3(1, 0, 0));
	}
}
