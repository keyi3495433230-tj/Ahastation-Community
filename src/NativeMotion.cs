using System.Numerics;
using X=Microsoft.UI.Xaml;
using A=Microsoft.UI.Xaml.Hosting;
namespace CardReview;
public sealed partial class ReviewForm
{
    readonly Windows.UI.ViewManagement.UISettings motionSettings=new();
    bool MotionEnabled=>nativeAnimations&&motionSettings.AnimationsEnabled;
    void MotionEnter(X.FrameworkElement element,float distance=10)
    {
        A.ElementCompositionPreview.SetIsTranslationEnabled(element,true);
        var visual=A.ElementCompositionPreview.GetElementVisual(element);visual.Properties.InsertVector3("Translation",Vector3.Zero);visual.StopAnimation("Opacity");visual.StopAnimation("Translation");
        if(!MotionEnabled){visual.Opacity=1;visual.Properties.InsertVector3("Translation",Vector3.Zero);return;}
        var compositor=visual.Compositor;var ease=compositor.CreateCubicBezierEasingFunction(new Vector2(.16f,1),new Vector2(.3f,1));
        var fade=compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,0);fade.InsertKeyFrame(1,1,ease);fade.Duration=TimeSpan.FromMilliseconds(190);visual.StartAnimation("Opacity",fade);
        var move=compositor.CreateVector3KeyFrameAnimation();move.InsertKeyFrame(0,new Vector3(0,distance,0));move.InsertKeyFrame(1,Vector3.Zero,ease);move.Duration=TimeSpan.FromMilliseconds(220);visual.StartAnimation("Translation",move);
    }
    void MotionExit(X.FrameworkElement element)
    {
        A.ElementCompositionPreview.SetIsTranslationEnabled(element,true);var visual=A.ElementCompositionPreview.GetElementVisual(element);visual.Properties.InsertVector3("Translation",Vector3.Zero);visual.StopAnimation("Opacity");visual.StopAnimation("Translation");
        if(!MotionEnabled)return;
        var fade=visual.Compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(1,0);fade.Duration=TimeSpan.FromMilliseconds(120);visual.StartAnimation("Opacity",fade);
        var move=visual.Compositor.CreateVector3KeyFrameAnimation();move.InsertKeyFrame(1,new Vector3(0,10,0));move.Duration=TimeSpan.FromMilliseconds(120);visual.StartAnimation("Translation",move);
    }
    void MotionButton(Microsoft.UI.Xaml.Controls.Button button)
    {
        void Scale(float value){var visual=A.ElementCompositionPreview.GetElementVisual(button);visual.StopAnimation("Scale");if(!MotionEnabled){visual.Scale=Vector3.One;return;}visual.CenterPoint=new Vector3((float)button.ActualWidth/2,(float)button.ActualHeight/2,0);var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.InsertKeyFrame(1,new Vector3(value,value,1));animation.Duration=TimeSpan.FromMilliseconds(100);visual.StartAnimation("Scale",animation);}
        button.PointerEntered+=(_,_)=>Scale(1.015f);button.PointerExited+=(_,_)=>Scale(1);button.PointerPressed+=(_,_)=>Scale(.97f);button.PointerReleased+=(_,_)=>Scale(1);button.PointerCanceled+=(_,_)=>Scale(1);button.PointerCaptureLost+=(_,_)=>Scale(1);button.LostFocus+=(_,_)=>Scale(1);
    }
}
