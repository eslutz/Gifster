import Foundation

public enum CreditProductCatalog {
  public static let productIDs = [
    "dev.ericslutz.gifforge.credits.10",
    "dev.ericslutz.gifforge.credits.25",
    "dev.ericslutz.gifforge.credits.60"
  ]
}

#if canImport(StoreKit)
import StoreKit

@available(iOS 15.0, macOS 12.0, *)
public struct StoreKitCreditPurchaseService: Sendable {
  private let backendClient: GifForgeBackendClient
  private let appAccountToken: UUID

  public init(backendClient: GifForgeBackendClient, appAccountToken: UUID) {
    self.backendClient = backendClient
    self.appAccountToken = appAccountToken
  }

  public func products() async throws -> [Product] {
    try await Product.products(for: CreditProductCatalog.productIDs)
  }

  public func purchase(_ product: Product) async throws -> BackendIAPTransactionResult? {
    let result = try await product.purchase(options: [.appAccountToken(appAccountToken)])
    switch result {
    case let .success(verification):
      let verified = try verifiedTransaction(from: verification)
      let grant = try await backendClient.submitIAPTransaction(
        productID: verified.transaction.productID,
        signedTransaction: verified.signedTransaction
      )
      await verified.transaction.finish()
      return grant
    case .pending:
      return nil
    case .userCancelled:
      return nil
    @unknown default:
      return nil
    }
  }

  public func submitUnfinishedTransactions() async throws -> [BackendIAPTransactionResult] {
    var grants: [BackendIAPTransactionResult] = []
    for await verification in Transaction.unfinished {
      let verified = try verifiedTransaction(from: verification)
      let grant = try await backendClient.submitIAPTransaction(
        productID: verified.transaction.productID,
        signedTransaction: verified.signedTransaction
      )
      await verified.transaction.finish()
      grants.append(grant)
    }
    return grants
  }

  private func verifiedTransaction(
    from verification: VerificationResult<Transaction>
  ) throws -> (transaction: Transaction, signedTransaction: String) {
    switch verification {
    case let .verified(transaction):
      (transaction, verification.jwsRepresentation)
    case .unverified:
      throw GifForgeError.backendRejected(statusCode: 401, message: "StoreKit transaction could not be verified.")
    }
  }
}
#endif
