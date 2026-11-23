using System;
using Cairo;
using Vintagestory.API.Client;

namespace Egocarib.AutoMapMarkers.GUI
{
    /// <summary>
    /// A small single-line number box that works inside the scrolling marker list.
    /// The stock text input bakes its frame into the dialog's static texture (so it would not
    /// scroll) and turns scissoring off after drawing its text (which would let later rows draw
    /// outside the list). This one draws its frame in the interactive pass, hides itself while
    /// it is not fully inside the list, and restores the list's clipping afterwards.
    /// Only digits are accepted.
    /// </summary>
    public class GuiElementScrollSafeNumberInput : GuiElementTextInput
    {
        private readonly ElementBounds clipBounds;
        private LoadedTexture frameTexture;

        /// <summary>Called when the player is done typing (Enter, or clicking elsewhere).</summary>
        public Action OnCommit;

        public GuiElementScrollSafeNumberInput(ICoreClientAPI capi, ElementBounds bounds, Action<string> onTextChanged,
            CairoFont font, ElementBounds clipBounds)
            : base(capi, bounds, onTextChanged, font)
        {
            this.clipBounds = clipBounds;
            frameTexture = new LoadedTexture(capi);
            OnTryTextChangeText = lines =>
            {
                foreach (string line in lines)
                {
                    foreach (char c in line)
                    {
                        if (c < '0' || c > '9') return false;
                    }
                }
                return true;
            };
            SetMaxLength(4);
        }

        public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
        {
            // Build the stock textures, but keep the stock frame off the dialog background.
            using (ImageSurface dummySurface = new ImageSurface(Format.Argb32, 1, 1))
            using (Context dummyCtx = genContext(dummySurface))
            {
                base.ComposeElements(dummyCtx, dummySurface);
            }

            int width = (int)Bounds.OuterWidth;
            int height = (int)Bounds.OuterHeight;
            if (width <= 0 || height <= 0) return;

            ImageSurface surface = new ImageSurface(Format.Argb32, width, height);
            Context ctx = genContext(surface);
            RoundRectangle(ctx, 0, 0, width, height, 1);
            ctx.SetSourceRGBA(0, 0, 0, 0.3);
            ctx.Fill();
            EmbossRoundRectangle(ctx, 0, 0, width, height, 1, 1, 0.9f, 0.8f, true, 0f);
            generateTexture(surface, ref frameTexture);
            ctx.Dispose();
            surface.Dispose();
        }

        private bool FullyInsideClip()
        {
            if (clipBounds == null) return true;
            return Bounds.renderY >= clipBounds.renderY - 1
                && Bounds.renderY + Bounds.OuterHeight <= clipBounds.renderY + clipBounds.OuterHeight + 1;
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (!FullyInsideClip()) return;

            api.Render.Render2DTexturePremultipliedAlpha(frameTexture.TextureId,
                (int)Bounds.renderX, (int)Bounds.renderY, (int)Bounds.OuterWidth, (int)Bounds.OuterHeight);
            // The stock focus highlight / placeholder position is computed once at compose time;
            // follow the row when the list scrolls.
            highlightBounds?.CalcWorldBounds();
            base.RenderInteractiveElements(deltaTime);

            // The stock input switched scissoring off; put the list's clip region back.
            if (clipBounds != null)
            {
                api.Render.GlScissor(
                    (int)clipBounds.renderX,
                    (int)(api.Render.FrameHeight - clipBounds.renderY - clipBounds.InnerHeight),
                    Math.Max(0, clipBounds.OuterWidthInt),
                    Math.Max(0, clipBounds.OuterHeightInt));
                api.Render.GlScissorFlag(true);
            }
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            if (!FullyInsideClip()) return;
            base.OnMouseDownOnElement(api, args);
        }

        public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
        {
            if (HasFocus && (args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter))
            {
                OnCommit?.Invoke();
                args.Handled = true;
                return;
            }
            base.OnKeyDown(api, args);
        }

        public override void OnFocusLost()
        {
            base.OnFocusLost();
            OnCommit?.Invoke();
        }

        public override void Dispose()
        {
            base.Dispose();
            frameTexture?.Dispose();
        }
    }

    public static class GuiComposerScrollSafeNumberInputExtensions
    {
        /// <summary>
        /// Adds a digits-only box for use inside a scrolling clip region. Retrieve it with the
        /// stock GetTextInput(key) and cast to GuiElementScrollSafeNumberInput for OnCommit.
        /// </summary>
        public static GuiComposer AddScrollSafeNumberInput(this GuiComposer composer, ElementBounds bounds,
            Action<string> onTextChanged, ElementBounds clipBounds, string key = null)
        {
            if (!composer.Composed)
            {
                composer.AddInteractiveElement(new GuiElementScrollSafeNumberInput(composer.Api, bounds, onTextChanged,
                    CairoFont.WhiteSmallText(), clipBounds), key);
            }
            return composer;
        }
    }
}
