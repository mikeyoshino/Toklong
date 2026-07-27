# Extractable components

## AppShellBottomNavigation
- Source: `src/Toklong.Mobile/AppShell.xaml`
- Category: layout
- Description: Three-tab navigation for transactions, activity, and account.
- Extractable props: activeItem (string, default: "transactions")
- Hardcoded: Thai tab labels, icon asset names, TOKLONG brand colors.

## BrandLockupView
- Source: `src/Toklong.Mobile/Controls/BrandLockupView.xaml`
- Category: basic
- Description: TOKLONG mark, wordmark, and Thai strapline.
- Extractable props: none
- Hardcoded: brand mark, wordmark, strapline, gradient, spacing.

## FormLabelView
- Source: `src/Toklong.Mobile/Controls/FormLabelView.xaml`
- Category: basic
- Description: Consistent field label and required asterisk.
- Extractable props: text (string), isRequired (boolean, default: false)
- Hardcoded: typography, spacing, required color.

No separate Create Offer layout component is currently shared across screens. Basic Button, Entry, Card, Picker, and sheet patterns are resource styles in `App.xaml` and should remain inline in a draft rather than extracted.
