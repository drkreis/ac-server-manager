# Accessibility

AC Server Manager aims to make local server management understandable and usable with a keyboard as well as a mouse. Accessibility work is ongoing. The application has not undergone a comprehensive assistive-technology audit, and no accessibility conformance claim is made.

## Current interface

The application is a Windows WPF desktop program tested on Windows 11 x64. It has English and Russian interfaces, resizable windows, text labels for major actions and standard editable fields. Standard controls provide keyboard focus navigation with Tab and Shift+Tab. Confirmation dialogs support Escape to cancel and use Cancel as the default choice. Diagnostic findings include text explanations and actions, in addition to severity colors.

## Known limits

- The custom dark theme has no dedicated high-contrast or large-text mode.
- Some secondary labels and technical fields use small text. Display scaling and varied monitor configurations need broader testing.
- Screen-reader behavior, focus order and custom controls have not been comprehensively verified.
- Car previews, track previews and skin swatches are visual. Text names are available, but images do not provide a complete nonvisual description of their appearance.
- Some detailed configuration values still use technical IDs or raw server output.

## Report a barrier

Use the [accessibility report form](https://github.com/drkreis/ac-server-manager/issues/new?template=accessibility.yml). Describe the task you were trying to complete, the control involved, the application version, Windows version, display scaling and any relevant assistive technology. Explain what happened and what would make the task possible. A screenshot or short recording can help, but is not required.

Reports and pull requests that improve keyboard operation, focus visibility, readable labels, contrast and assistive-technology support are welcome. Please follow [CONTRIBUTING.md](CONTRIBUTING.md) and remove personal information from attachments.
