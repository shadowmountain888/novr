using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRGameplayUIBehaviour : UIRenderedCanvasBehavior
{
    private static readonly string[] ChatSpacerNames = { "LeftSpace", "MiddleSpace", "RightSpace" };

    private Image[] _chatSpacerImages;

    private void Update()
    {
        transform.localScale = new Vector3(0.003f, 0.003f, 0.003f);
        transform.position = new Vector3(0f, 0f, 3f);
        HideChatLayoutSpacers();
    }

    // ChatCanvas/TopPanel lays its contents out with three HorizontalLayoutGroup spacers -
    // LeftSpace, MiddleSpace and RightSpace. Each carries an opaque white Image with no sprite,
    // which is harmless in the stock screen-space canvas but renders as a solid white panel once
    // this behaviour converts the canvas to world space on the VR UI layer: at 0.003 scale and 3 m
    // out, a 640x170 spacer is roughly 1.9 m wide, so the left and right ones sit in the pilot's
    // peripheral vision. Only the Image is disabled; the layout itself is untouched.
    private void HideChatLayoutSpacers()
    {
        if (ModConfiguration.Instance?.HideChatLayoutSpacers.Value != true) return;

        if (_chatSpacerImages == null)
        {
            var topPanel = FindChildStartingWith(transform, "TopPanel");
            if (topPanel == null) return;

            var found = new System.Collections.Generic.List<Image>(ChatSpacerNames.Length);
            foreach (var spacerName in ChatSpacerNames)
            {
                var spacer = FindChildStartingWith(topPanel, spacerName);
                if (spacer == null) continue;
                if (!spacer.TryGetComponent<Image>(out var spacerImage)) continue;

                found.Add(spacerImage);
            }

            if (found.Count == 0) return;
            _chatSpacerImages = found.ToArray();
        }

        // Re-asserted every frame rather than latched once: MessageUI rebuilds the chat bar as
        // messages arrive and re-enables these Images, which brought the white panels back.
        for (var index = 0; index < _chatSpacerImages.Length; index++)
        {
            var spacerImage = _chatSpacerImages[index];
            if (spacerImage != null && spacerImage.enabled)
            {
                spacerImage.enabled = false;
            }
        }
    }
}
