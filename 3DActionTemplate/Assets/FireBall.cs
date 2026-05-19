using UnityEngine;

public class FireBall : MonoBehaviour
{
    [SerializeField]
    private GameObject _effect;
    [SerializeField]
    private float _power = 1000f;

    private Vector3 _dir;
    private bool _isShooting = false;

    private void OnTriggerEnter(Collider other)
    {
        var colliders = Physics.OverlapSphere(transform.position, 5f);
        foreach (var collider in colliders)
        {
            var rigidbody = collider.GetComponent<Rigidbody>();
            if(rigidbody != null)
            {
                rigidbody.AddForce(collider.transform.position - transform.position * _power);
            }
        }
        Instantiate(_effect, transform.position, transform.rotation);
        Destroy(gameObject);
    }

    private void Update()
    {
        if(_isShooting)
        {
            transform.position += _dir * Time.deltaTime;
        }
    }

    public void Shoot(Vector3 dir)
    {
        _dir = dir;
        _isShooting = true;
    }   
}
