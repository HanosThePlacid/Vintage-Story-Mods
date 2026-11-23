using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Egocarib.AutoMapMarkers.GUI
{
    /// <summary>
    /// A stock slider whose track renders in the interactive pass, so it scrolls correctly
    /// inside clip regions. The stock GuiElementSlider bakes its dark track into the dialog's
    /// static texture at compose time; when the list scrolls, the handle and fill move but the
    /// track stays where the row was when the dialog was composed.
    /// </summary>
    public class GuiElementScrollSafeSlider : GuiElementSlider
    {
        private LoadedTexture trackTexture;

        public GuiElementScrollSafeSlider(ICoreClientAPI capi, ActionConsumable<int> onNewSliderValue, ElementBounds bounds)
            : base(capi, onNewSliderValue, bounds)
        {
            trackTexture = new LoadedTexture(capi);
        }

        public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
        {
            // Let the stock slider build its handle/fill/text textures, but point its static
            // track drawing at a throwaway surface so nothing lands on the dialog background.
            using (ImageSurface dummySurface = new ImageSurface(Format.Argb32, 1, 1))
            using (Context dummyCtx = genContext(dummySurface))
            {
                base.ComposeElements(dummyCtx, dummySurface);
            }

            int width = (int)Bounds.InnerWidth;
            int height = (int)Bounds.InnerHeight;
            if (width <= 0 || height <= 0) return;

            ImageSurface surface = new ImageSurface(Format.Argb32, width, height);
            Context ctx = genContext(surface);

            RoundRectangle(ctx, 0, 0, width, height, 1);
            ctx.SetSourceRGBA(0, 0, 0, 0.2);
            ctx.Fill();
            // Stronger emboss with no alpha offset, compensating for a transparent surface
            // instead of the opaque dialog background (same approach as GuiElementInteractiveSwitch).
            EmbossRoundRectangle(ctx, 0, 0, width, height, 1, 1, 0.9f, 0.8f, true, 0f);

            generateTexture(surface, ref trackTexture);
            ctx.Dispose();
            surface.Dispose();
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            api.Render.Render2DTexturePremultipliedAlpha(trackTexture.TextureId,
                (int)Bounds.renderX, (int)Bounds.renderY, (int)Bounds.InnerWidth, (int)Bounds.InnerHeight);
            base.RenderInteractiveElements(deltaTime);
        }

        public override void Dispose()
        {
            base.Dispose();
            trackTexture?.Dispose();
        }
    }

    public static class GuiComposerScrollSafeSliderExtensions
    {
        /// <summary>
        /// Adds a slider that scrolls correctly inside clip regions. Retrieve it with the stock
        /// GetSlider(key); it is a GuiElementSlider.
        /// </summary>
        public static GuiComposer AddScrollSafeSlider(this GuiComposer composer, ActionConsumable<int> onNewSliderValue, ElementBounds bounds, string key = null)
        {
            if (!composer.Composed)
            {
                composer.AddInteractiveElement(new GuiElementScrollSafeSlider(composer.Api, onNewSliderValue, bounds), key);
            }
            return composer;
        }
    }
}
