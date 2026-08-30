using System;
using Microsoft.UI.Xaml.Controls;

namespace Noxygen.Services.Contracts;

public interface INavigationService
{
    void Initialize(Frame frame);
    bool NavigateTo(Type pageType, object? parameter = null);
    bool GoBack();
}