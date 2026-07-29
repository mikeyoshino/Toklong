# Seller Detail Cost Visibility Design

## Goal

Reduce unnecessary financial detail on the seller transaction page while
retaining the information the seller needs to fulfill the order and understand
their payout.

## Approved UI

The seller-only product detail disclosure keeps:

- product price;
- shipping service;
- seller net amount.

It does not show:

- shipping fee;
- parcel insurance fee;
- parcel insured value.

The buyer cost disclosure remains unchanged and continues to show the amounts
the buyer paid where required.

## Scope and Data

This is a role-specific presentation change in
`TransactionDetailPage.xaml`. The API response, immutable paid transaction
snapshot, money calculations, shipping operation, payout amount, audit events,
and state transitions remain unchanged.

## Accessibility

Removed seller-only rows must not remain as hidden or duplicated accessible
elements. The remaining labels keep their existing readable order and color
contrast.

## Verification

Add or update a UI layout regression test that:

- rejects the three removed bindings inside `SellerPayoutDisclosure`;
- retains the product price, shipping service, and seller net bindings;
- verifies the buyer cost disclosure remains present.

Build the iOS simulator target and visually confirm the seller disclosure.

