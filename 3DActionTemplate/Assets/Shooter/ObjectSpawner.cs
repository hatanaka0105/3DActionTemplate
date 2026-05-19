using UnityEngine;

    public class ObjectSpawner : MonoBehaviour
    {
        [SerializeField]
        private GameObject _spawnObj = null;
        [SerializeField]
        private float _interval = 1.0f;
        [SerializeField]
        private BoxCollider _spawnRange = null;
        [SerializeField]
        private bool _randomizeX = false;
        [SerializeField]
        private bool _randomizeY = false;
        [SerializeField]
        private bool _randomizeZ = false;
        [SerializeField]
        private Transform _parent = null;
        [SerializeField]
        private int _spawnLimit = 100;
        [SerializeField]
        private bool _isOnceSpawn = false;
        [SerializeField]
        private Vector3 _direction;

        private float _time = 0.0f;
        private Vector3 _range = Vector3.zero;
        private int spawnCount = 0;

        private void Start()
        {
            _range = (_spawnRange.size * 0.5f);

            if (_isOnceSpawn && _spawnObj != null)
            {
                if (_randomizeX || _randomizeY || _randomizeZ)
                {
                    for (int i = 0; i < _spawnLimit; i++)
                    {
                        SpawnRandomRange();
                    }
                }
                else
                {
                    for (int i = 0; i < _spawnLimit; i++)
                    {
                        Spawn(transform.position);
                    }
                }
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (_isOnceSpawn)
            {
                return;
            }

            _time += Time.deltaTime;

            if (_spawnObj == null || _time < _interval || spawnCount > _spawnLimit)
            {
                return;
            }

            if (_randomizeX || _randomizeY || _randomizeZ)
            {
                SpawnRandomRange();
            }
            else
            {
                Spawn(transform.position);
            }

            spawnCount++;
            _time = 0.0f;
        }

        public void SpawnRandomRange()
        {
            float x = 0f;
            if(_randomizeX)
            {
                x = Random.Range(-_range.x, _range.x) + transform.position.x;
            }
            else
            {
                x = transform.position.x;
            }

            float y = 0f;
            if(_randomizeY)
            {
                y = Random.Range(-_range.y, _range.y) + transform.position.y;
            }
            else
            {
                y = transform.position.y;
            }

            float z = 0f;
            if(_randomizeZ)
            {
                z = Random.Range(-_range.z, _range.z) + transform.position.z;
            }
            else
            {
                z = transform.position.z;
            }

            Spawn(new Vector3(x, y, z));
        }

        public void Spawn(Vector3 spawnPos)
        {
            var obj = Instantiate(_spawnObj, spawnPos, Quaternion.identity, _parent);
            var spawn = obj.GetComponent<ISpawn>();
            if(spawn != null)
            {
                spawn.Rotate(_direction);
                spawn.StartMove(50, 100);
            }
        }
    }
