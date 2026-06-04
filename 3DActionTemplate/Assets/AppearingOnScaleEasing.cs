using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
    /// これをつけると登場するとき拡縮でニュッて出てくるようになる
    /// 細かいことはできない
    /// アニメーションと違ってTransformに従属する
    /// </summary>
    public class AppearingOnScaleEasing : MonoBehaviour
    {
        [SerializeField]
        private float _scale = 1f;

        [SerializeField]
        private float _scaleDuration = 0.5f;

        [SerializeField]
        private Ease _easeType = 0;

        [SerializeField]
        private bool _ignoreTimeScale;

        [SerializeField]
        private bool _ignoreX = false;

        [SerializeField]
        private bool _ignoreY = false;

        [SerializeField]
        private bool _ignoreZ = false;

        [SerializeField]
        private MMF_Player _appearingFeedback;

        [SerializeField]
        private MMF_Player _disappearingFeedback;

        private Tween _tween;

        private void OnEnable()
        {
            var button = gameObject.GetComponent<Button>();
            if (button != null)
            {
                button.enabled = false;
            }

            Vector3 defaultAxes = Vector3.zero;
            if (_ignoreX)
            {
                defaultAxes.x = _scale;
            }
            if (_ignoreY)
            {
                defaultAxes.y = _scale;
            }
            if (_ignoreZ)
            {
                defaultAxes.z = _scale;
            }
            transform.localScale = defaultAxes;

            Vector3 tweenAxes = Vector3.one;
            _tween = transform.DOScale(Vector3.one * _scale, _scaleDuration)
                .SetEase(_easeType)
                .SetUpdate(_ignoreTimeScale)
                .SetLink(gameObject)
                .OnComplete(OnComplete);
        }

        public void DisableWithTween()
        {
            _disappearingFeedback?.PlayFeedbacks();
            _tween = transform.DOScale(Vector3.zero, _scaleDuration)
                .SetEase(_easeType)
                .SetUpdate(_ignoreTimeScale)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (this != null && gameObject != null)
                    {
                        gameObject.SetActive(false);
                    }
                });
        }

        private void OnDisable()
        {
            // Tween破棄
            if (DOTween.instance != null)
            {
                _tween?.Kill();
            }
        }

        private void OnComplete()
        {
            // NOTE: Button が無いオブジェクトに付与されているケースで GetComponent<Button>() が
            // null を返して NRE になることがあるため、null チェックを追加する。
            if (this == null || gameObject == null)
            {
                return;
            }
            var button = gameObject.GetComponent<Button>();
            if (button != null)
            {
                button.enabled = true;
            }
        }
    }
