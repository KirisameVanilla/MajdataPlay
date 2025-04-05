using MajdataPlay.IO;
using MajdataPlay.Types;
using MajdataPlay.Utils;
using TMPro;
using UnityEngine;

namespace MajdataPlay
{
    public class MessagePanelController: MonoBehaviour
    {
        public TMP_Text MessageText;
        public GameObject MessagePanel;
        public bool Active = false;

        private void Awake()
        {
            MajInstances.MessagePanelController = this;
        }

        // Start is called before the first frame update
        void Start()
        {
            MessageText.gameObject.SetActive(Active);
            MessagePanel.gameObject.SetActive(Active);
            DontDestroyOnLoad(this);
        }

        void LateUpdate()
        {
            if (!Active) return;
        }

        public void Notify(string text, float duration = 2f)
        {
            FadeIn();
            SetMessageText(text);
            Invoke(nameof(FadeOut), duration);
        }

        public void FadeOut()
        {
            Active = false;
            MessageText.gameObject.SetActive(Active);
            MessagePanel.gameObject.SetActive(Active);
        }

        public void FadeIn()
        {
            Active = true;
            MessagePanel.gameObject.SetActive(Active);
            MessageText.gameObject.SetActive(Active);
        }

        public void SetMessageText(string text, Color? color = null)
        {
            MessageText.text += "\n" + text;
            MessageText.color = color ?? Color.white;
        }
    }
}
