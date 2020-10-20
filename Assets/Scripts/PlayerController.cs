using NetworkMessages;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float speed = 2;
    public string id;
    public NetworkClient network;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //Update only if local
        if (id == network.localID)
        {
            float xDirection = 0;
            float zDirection = 0;
            xDirection = Input.GetAxis("Horizontal") * speed;
            zDirection = Input.GetAxis("Vertical") * speed;
            transform.position = new Vector3(xDirection * speed, 0, zDirection * speed);
        }
        //Quit on Escape Key
        if (Input.GetKey(KeyCode.Escape)) { Application.Quit(); }
    }
}
