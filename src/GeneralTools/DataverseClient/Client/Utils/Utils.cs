﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;
using Microsoft.PowerPlatform.Dataverse.Client.Model;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk;
using System.Dynamic;
using Microsoft.PowerPlatform.Dataverse.Client.Utils;

namespace Microsoft.PowerPlatform.Dataverse.Client
{
    /// <summary>
    /// Utility functions the ServiceClient assembly.
    /// </summary>
    public class Utilities
    {
        private Utilities() { }

        /// <summary>
        /// Returns the file version of passed "executing Assembly"
        /// </summary>
        /// <param name="executingAssembly">The assembly whose version is required.</param>
        /// <returns></returns>
        public static Version GetFileVersion(Assembly executingAssembly)
        {
            try
            {
                if (executingAssembly != null)
                {
                    AssemblyName asmName = new AssemblyName(executingAssembly.FullName);
                    Version fileVersion = asmName.Version;

                    // try to get the build version
                    string localPath = string.Empty;

                    Uri fileUri = null;
                    if (Uri.TryCreate(executingAssembly.CodeBase, UriKind.Absolute, out fileUri))
                    {
                        if (fileUri.IsFile)
                            localPath = fileUri.LocalPath;

                        if (!string.IsNullOrEmpty(localPath))
                            if (System.IO.File.Exists(localPath))
                            {
                                FileVersionInfo fv = FileVersionInfo.GetVersionInfo(localPath);
                                if (fv != null)
                                {
                                    fileVersion = new Version(fv.FileVersion);
                                }
                            }
                    }
                    return fileVersion;
                }
            }
            catch { }

            return null;
        }

        internal static DiscoveryServer GetDiscoveryServerByUri(Uri orgUri)
        {
            if (orgUri != null)
            {
                string OnlineRegon = string.Empty;
                string OrgName = string.Empty;
                bool IsOnPrem = false;
                Utilities.GetOrgnameAndOnlineRegionFromServiceUri(orgUri, out OnlineRegon, out OrgName, out IsOnPrem);
                if (!string.IsNullOrEmpty(OnlineRegon))
                {
                    using (DiscoveryServers discoSvcs = new DiscoveryServers())
                    {
                        return discoSvcs.GetServerByShortName(OnlineRegon);
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// Get the organization name and on-line region from the Uri
        /// </summary>
        /// <param name="serviceUri">Service Uri to parse</param>
        /// <param name="isOnPrem">if OnPrem, will be set to true, else false.</param>
        /// <param name="onlineRegion">Name of the CRM on line Region serving this request</param>
        /// <param name="organizationName">Name of the Organization extracted from the Service URI</param>
        public static void GetOrgnameAndOnlineRegionFromServiceUri(Uri serviceUri, out string onlineRegion, out string organizationName, out bool isOnPrem)
        {
            isOnPrem = false;
            onlineRegion = string.Empty;
            organizationName = string.Empty;

            //support for detecting a Online URI in the path and rerouting to use that..
            if (IsValidOnlineHost(serviceUri))
            {
                try
                {
                    // Determine deployment region from Uri
                    List<string> elements = new List<string>(serviceUri.Host.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries));
                    organizationName = elements[0];
                    elements.RemoveAt(0); // remove the first ( org name ) from the Uri.


                    // construct Prospective CRM Online path.
                    System.Text.StringBuilder buildPath = new System.Text.StringBuilder();
                    foreach (var item in elements)
                    {
                        if (item.Equals("api"))
                            continue; // Skip the .api. when running via this path.
                        buildPath.AppendFormat("{0}.", item);
                    }
                    string crmKey = buildPath.ToString().TrimEnd('.').TrimEnd('/');
                    buildPath.Clear();
                    if (!string.IsNullOrEmpty(crmKey))
                    {
                        using (DiscoveryServers discoSvcs = new DiscoveryServers())
                        {
                            // drop in the discovery region if it can be determined.  if not, default to scanning.
                            var locatedDiscoServer = discoSvcs.OSDPServers.Where(w => w.DiscoveryServerUri != null && w.DiscoveryServerUri.Host.Contains(crmKey)).FirstOrDefault();
                            if (locatedDiscoServer != null && !string.IsNullOrEmpty(locatedDiscoServer.ShortName))
                                onlineRegion = locatedDiscoServer.ShortName;
                        }
                    }
                    isOnPrem = false;
                }
                finally
                { }
            }
            else
            {
                isOnPrem = true;
                //Setting organization for the AD/Onpremise Oauth/IFD
                if (serviceUri.Segments.Count() >= 2)
                {
                    organizationName = serviceUri.Segments[1].TrimEnd('/'); // Fix for bug 294040 http://vstfmbs:8080/tfs/web/wi.aspx?pcguid=12e6d33f-1461-4da4-b3d9-5517a4567489&id=294040
                }
            }

        }

        /// <summary>
        /// returns ( if possible ) the org detail for a given organization name from the list of orgs in discovery
        /// </summary>
        /// <param name="orgList">OrgList to Parse though</param>
        /// <param name="organizationName">Name to find</param>
        /// <returns>Found Organization Instance or Null</returns>
        public static OrgByServer DeterminOrgDataFromOrgInfo(OrgList orgList, string organizationName)
        {
            OrgByServer orgDetail = orgList.OrgsList.Where(o => o.OrgDetail.UniqueName.Equals(organizationName, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
            if (orgDetail == null)
                orgDetail = orgList.OrgsList.Where(o => o.OrgDetail.FriendlyName.Equals(organizationName, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();

            // still not found... try by URI name.
            if (orgDetail == null)
            {
                string formatedOrgName = string.Format("://{0}.", organizationName).ToLowerInvariant();
                orgDetail = orgList.OrgsList.Where(o => o.OrgDetail.Endpoints[EndpointType.WebApplication].Contains(formatedOrgName)).FirstOrDefault();
            }
            return orgDetail;
        }

        /// <summary>
        /// returns ( if possible ) the org detail for a given organization name from the list of orgs in discovery
        /// </summary>
        /// <param name="orgList">OrgList to Parse though</param>
        /// <param name="organizationName">Name to find</param>
        /// <returns>Found Organization Instance or Null</returns>
        public static OrganizationDetail DeterminOrgDataFromOrgInfo(OrganizationDetailCollection orgList, string organizationName)
        {
            OrganizationDetail orgDetail = orgList.Where(o => o.UniqueName.Equals(organizationName, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
            if (orgDetail == null)
                orgDetail = orgList.Where(o => o.FriendlyName.Equals(organizationName, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();

            // still not found... try by URI name.
            if (orgDetail == null)
            {
                string formatedOrgName = string.Format("://{0}.", organizationName).ToLowerInvariant();
                orgDetail = orgList.Where(o => o.Endpoints[EndpointType.WebApplication].Contains(formatedOrgName)).FirstOrDefault();
            }
            return orgDetail;
        }

        /// <summary>
        /// Parses an OrgURI to determine what the supporting discovery server is.
        /// </summary>
        /// <param name="serviceUri">Service Uri to parse</param>
        /// <param name="Geo">Geo Code for region (Optional)</param>
        /// <param name="isOnPrem">if OnPrem, will be set to true, else false.</param>
        public static DiscoveryServer DeterminDiscoveryDataFromOrgDetail(Uri serviceUri, out bool isOnPrem, string Geo = null)
        {
            isOnPrem = false;
            //support for detecting a Live/Online URI in the path and rerouting to use that..
            if (IsValidOnlineHost(serviceUri))
            {
                // Check for Geo code and to make sure that the region is not on our internal list.
                if (!string.IsNullOrEmpty(Geo)
                    && !(serviceUri.Host.ToUpperInvariant().Contains("CRMLIVETIE.COM")
                    || serviceUri.Host.ToUpperInvariant().Contains("CRMLIVETODAY.COM"))
                    )
                {
                    using (DiscoveryServers discoSvcs = new DiscoveryServers())
                    {
                        // Find by Geo, if null fall though to next check
                        var locatedDiscoServer = discoSvcs.OSDPServers.Where(w => !string.IsNullOrEmpty(w.GeoCode) && w.GeoCode == Geo).FirstOrDefault();
                        if (locatedDiscoServer != null && !string.IsNullOrEmpty(locatedDiscoServer.ShortName))
                            return locatedDiscoServer;
                    }
                }

                try
                {
                    isOnPrem = false;

                    // Determine deployment region from Uri
                    List<string> elements = new List<string>(serviceUri.Host.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries));
                    elements.RemoveAt(0); // remove the first ( org name ) from the Uri.


                    // construct Prospective Dataverse Online path.
                    System.Text.StringBuilder buildPath = new System.Text.StringBuilder();
                    foreach (var item in elements)
                    {
                        if (item.Equals("api"))
                            continue; // Skip the .api. when running via this path.
                        buildPath.AppendFormat("{0}.", item);
                    }
                    string crmKey = buildPath.ToString().TrimEnd('.').TrimEnd('/');
                    buildPath.Clear();
                    if (!string.IsNullOrEmpty(crmKey))
                    {
                        using (DiscoveryServers discoSvcs = new DiscoveryServers())
                        {
                            // drop in the discovery region if it can be determined.  if not, default to scanning.
                            var locatedDiscoServer = discoSvcs.OSDPServers.Where(w => w.DiscoveryServerUri != null && w.DiscoveryServerUri.Host.Contains(crmKey)).FirstOrDefault();
                            if (locatedDiscoServer != null && !string.IsNullOrEmpty(locatedDiscoServer.ShortName))
                                return locatedDiscoServer;
                        }
                    }
                }
                finally
                { }
            }
            else
            {
                isOnPrem = true;
                return null;
            }
            return null;

        }

        /// <summary>
        /// Looks at the URL provided and determines if the URL is a valid online URI
        /// </summary>
        /// <param name="hostUri">URI to examine</param>
        /// <returns>Returns True if the URI is recognized as online, or false if not.</returns>
        public static bool IsValidOnlineHost(Uri hostUri)
        {
#if DEBUG
            if (hostUri.DnsSafeHost.ToUpperInvariant().Contains("DYNAMICS.COM")
                || hostUri.DnsSafeHost.ToUpperInvariant().Contains("DYNAMICS-INT.COM")
                || hostUri.DnsSafeHost.ToUpperInvariant().Contains("MICROSOFTDYNAMICS.DE")
                || hostUri.DnsSafeHost.ToUpperInvariant().Contains("MICROSOFTDYNAMICS.US")
                || hostUri.DnsSafeHost.ToUpperInvariant().Contains("APPSPLATFORM.US")
                || hostUri.DnsSafeHost.ToUpperInvariant().Contains("CRM.DYNAMICS.CN")
                || hostUri.DnsSafeHost.ToUpperInvariant().Contains("CRMLIVETIE.COM")
                || hostUri.DnsSafeHost.ToUpperInvariant().Contains("CRMLIVETODAY.COM"))
#else
			if (hostUri.DnsSafeHost.ToUpperInvariant().Contains("DYNAMICS.COM")
				|| hostUri.DnsSafeHost.ToUpperInvariant().Contains("MICROSOFTDYNAMICS.DE")
				|| hostUri.DnsSafeHost.ToUpperInvariant().Contains("MICROSOFTDYNAMICS.US")
				|| hostUri.DnsSafeHost.ToUpperInvariant().Contains("APPSPLATFORM.US")
				|| hostUri.DnsSafeHost.ToUpperInvariant().Contains("CRM.DYNAMICS.CN")
				|| hostUri.DnsSafeHost.ToUpperInvariant().Contains("DYNAMICS-INT.COM")) // Allows integration Test as well as PRD
#endif
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Determines if the request type can be translated to WebAPI
        /// This is a temp method to support the staged transition to the webAPI and will be removed or reintegrated with the overall pipeline at some point in the future.
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        internal static bool IsRequestValidForTranslationToWebAPI(OrganizationRequest req)
        {
            string RequestName = req.RequestName.ToLower();
            switch (RequestName)
            {
                case "create":
                case "update":
                case "delete":
                    return true;
                case "upsert":
                // Disabling WebAPI support for upsert right now due to issues with generating the response.

                // avoid bug in WebAPI around Support for key's as EntityRefeances //TODO: TEMP
                //Xrm.Sdk.Messages.UpsertRequest upsert = (Xrm.Sdk.Messages.UpsertRequest)req;
                //if (upsert.Target.KeyAttributes?.Any(a => a.Value is string) != true)
                //	return false;
                //else
                //return true;
                default:
                    return false;
            }
        }
        /// <summary>
        /// Parses an attribute array into a object that can be used to create a JSON request.
        /// </summary>
        /// <param name="sourceEntity">Entity to process</param>
        /// <param name="mUtil"></param>
        /// <returns></returns>
        internal static ExpandoObject ToExpandoObject(Entity sourceEntity , MetadataUtility mUtil)
        {
            dynamic expando = new ExpandoObject();

            // Check for primary Id info:
            if (sourceEntity.Id != Guid.Empty)
                sourceEntity = UpdateEntityAttributesForPrimaryId(sourceEntity, mUtil);

            AttributeCollection entityAttributes = sourceEntity.Attributes;
            if (!(entityAttributes != null) && (entityAttributes.Count > 0 ))
            {
                return expando;
            }

            var expandoObject = (IDictionary<string, object>)expando;
            var attributes = entityAttributes.ToArray();

            // this is used to support ActivityParties collections
            List<ExpandoObject> partiesCollection = null;

            foreach (var attrib in entityAttributes)
            {
                var keyValuePair = attrib;
                var value = keyValuePair.Value;
                var key = keyValuePair.Key;
                if (value is EntityReference entityReference)
                {
                    // Get Lookup attribute meta data for the ER to check for polymorphic relationship.
                    var attributeInfo = mUtil.GetAttributeMetadata(sourceEntity.LogicalName, key.ToLower());
                    if (attributeInfo is Xrm.Sdk.Metadata.LookupAttributeMetadata attribData)
                    {
                        // Now get relationship to make sure we use the correct name.
                        var eData = mUtil.GetEntityMetadata(Xrm.Sdk.Metadata.EntityFilters.Relationships, sourceEntity.LogicalName);
                        var ERNavName = eData.ManyToOneRelationships.FirstOrDefault(w => w.ReferencingAttribute.Equals(attribData.LogicalName) &&
                                                                                w.ReferencedEntity.Equals(entityReference.LogicalName))
                                                                                ?.ReferencingEntityNavigationPropertyName;
                        if (!string.IsNullOrEmpty(ERNavName))
                            key = ERNavName;

                        // Populate Key property
                        key = $"{key}@odata.bind";
                    }
                    else if (attributeInfo == null)
                    {
                        // Fault here.
                        throw new DataverseOperationException($"Entity Reference {key.ToLower()} was not found for entity {sourceEntity.LogicalName}.", null);
                    }

                    if (entityReference.Id == Guid.Empty)
                    {
                        value = null;
                    }
                    else
                    {
                        string entityReferanceValue = string.Empty;
                        // process ER Value
                        if (entityReference.KeyAttributes?.Any() == true)
                        {
                            entityReferanceValue = ParseAltKeyCollection(entityReference.KeyAttributes);
                        }
                        else
                        {
                            entityReferanceValue = entityReference.Id.ToString();
                        }


                        value = $"/{mUtil.GetEntityMetadata(Xrm.Sdk.Metadata.EntityFilters.Entity, entityReference.LogicalName).EntitySetName}({entityReferanceValue})";
                    }
                }
                else
                {
                    if (value is EntityCollection)
                    {
                        // try to get the participation type id from the key.
                        int PartyTypeId = PartyListHelper.GetParticipationtypeMasks(key);
                        bool isActivityParty = PartyTypeId != -1;  // if the partytypeID is -1 this is not a activity party collection.

                        if (isActivityParty && partiesCollection == null)
                            partiesCollection = new List<ExpandoObject>(); // Only build it when needed.

                        // build linked collection here.
                        foreach (var ent in (value as EntityCollection).Entities)
                        {
                            ExpandoObject rslt = ToExpandoObject(ent, mUtil);
                            if (isActivityParty)
                            {
                                var tempDict = ((IDictionary<string, object>)rslt);
                                tempDict.Add("participationtypemask", PartyTypeId);
                                partiesCollection.Add((ExpandoObject)tempDict);
                            }
                        }
                        if (isActivityParty)
                            continue;

                        // Note.. if this is not an activity party but instead an embedded entity.. this will fall though and fail with trying to embed an entity.
                    }
                    else
                    {
                        key = key.ToLower();
                        if (value is OptionSetValueCollection optionSetValues)
                        {
                            string mselectValueString = string.Empty;
                            foreach (var opt in optionSetValues)
                            {
                                mselectValueString += $"{opt.Value.ToString()},";
                            }
                            value = mselectValueString.Remove(mselectValueString.Length - 1);
                        }
                        else if (value is OptionSetValue optionSetValue)
                        {
                            value = optionSetValue.Value.ToString();
                        }
                        else if (value is DateTime dateTimeValue)
                        {
                            var attributeInfo = mUtil.GetAttributeMetadata(sourceEntity.LogicalName, key.ToLower());
                            if (attributeInfo is Xrm.Sdk.Metadata.DateTimeAttributeMetadata attribDateTimeData)
                            {
                                if (attribDateTimeData.Format == Xrm.Sdk.Metadata.DateTimeFormat.DateOnly)
                                {
                                    value = dateTimeValue.ToUniversalTime().ToString("yyyy-MM-dd");
                                }
                                else
                                {
                                    value = dateTimeValue.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                                }
                            }
                        }
                        else if (value is Money moneyValue)
                        {
                            value = moneyValue.Value;
                        }
                        else if (value is bool boolValue)
                        {
                            value = boolValue.ToString();
                        }
                        else if (value is Guid guidValue)
                        {
                            value = guidValue.ToString();
                        }
                        else if (value is null)
                        {
                            value = null;
                        }
                    }
                }
                expandoObject.Add(key, value);
            }

            // Check to see if this contained an activity party
            if (partiesCollection?.Count > 0)
            {
                expandoObject.Add($"{sourceEntity.LogicalName}_activity_parties", partiesCollection);
            }


            return (ExpandoObject)expandoObject;
        }

        /// <summary>
        /// checks to see if an attribute has been added to the collection containing the ID of the entity .
        /// this is required for the WebAPI to properly function.
        /// </summary>
        /// <param name="sourceEntity"></param>
        /// <param name="mUtil"></param>
        /// <returns></returns>
        private static Entity UpdateEntityAttributesForPrimaryId(Entity sourceEntity, MetadataUtility mUtil)
        {
            if ( sourceEntity.Id != Guid.Empty )
            {
                var entMeta = mUtil.GetEntityMetadata(sourceEntity.LogicalName);
                sourceEntity.Attributes[entMeta.PrimaryIdAttribute] = sourceEntity.Id;
            }
            return sourceEntity;
        }

        /// <summary>
        /// Handle general related entity collection construction
        /// </summary>
        /// <param name="rootExpando">Object being added too</param>
        /// <param name="entityName">parent entity</param>
        /// <param name="entityCollection">collection of relationships</param>
        /// <param name="mUtil">meta-data utility</param>
        /// <returns></returns>
        internal static ExpandoObject ReleatedEntitiesToExpandoObject(ExpandoObject rootExpando, string entityName, RelatedEntityCollection entityCollection, MetadataUtility mUtil)
        {
            if (rootExpando == null)
                return rootExpando;

            if ( entityCollection != null && entityCollection.Count == 0 )
            {
                // nothing to do, just return.
                return rootExpando;
            }

            foreach (var entItem in entityCollection)
            {
                string key = "";
                bool isArrayRequired = false;
                dynamic expando = new ExpandoObject();
                var expandoObject = (IDictionary<string, object>)expando;
                ExpandoObject childEntities = new ExpandoObject();

                List<ExpandoObject> childCollection = new List<ExpandoObject>();

                // Get the Entity relationship key and entity and reverse it back to the entity key name
                var eData = mUtil.GetEntityMetadata(Xrm.Sdk.Metadata.EntityFilters.Relationships, entItem.Value.Entities[0].LogicalName);

                // Find the relationship that is referenced.
                var ERM21 = eData.ManyToOneRelationships.FirstOrDefault(w1 => w1.SchemaName.ToLower().Equals(entItem.Key.SchemaName.ToLower()));
                var ERM2M = eData.ManyToManyRelationships.FirstOrDefault(w2 => w2.SchemaName.ToLower().Equals(entItem.Key.SchemaName.ToLower()));
                var ER12M = eData.OneToManyRelationships.FirstOrDefault(w3 => w3.SchemaName.ToLower().Equals(entItem.Key.SchemaName.ToLower()));

                // Determine which one hit
                if (ERM21 != null)
                {
                    isArrayRequired = true;
                    key = ERM21.ReferencedEntityNavigationPropertyName;
                }
                else if (ERM2M != null)
                {
                    isArrayRequired = true;
                    if (ERM2M.Entity1LogicalName.ToLower().Equals(entityName))
                    {
                        key = ERM2M.Entity1NavigationPropertyName;
                    }
                    else
                    {
                        key = ERM2M.Entity2NavigationPropertyName;
                    }
                }
                else if (ER12M != null)
                {
                    key = ER12M.ReferencingAttribute;
                }

                if ( string.IsNullOrEmpty(key) ) // Failed to find key
                {
                    throw new DataverseOperationException($"Relationship key {entItem.Key.SchemaName} cannot be found for related entities of {entityName}.");
                }

                foreach (var ent in entItem.Value.Entities)
                {
                    // Check to see if the entity itself has related entities
                    if (ent.RelatedEntities != null && ent.RelatedEntities.Count > 0)
                    {
                        childEntities = ReleatedEntitiesToExpandoObject(childEntities, entityName, ent.RelatedEntities, mUtil);
                    }

                    // generate object.
                    ExpandoObject ent1 = ToExpandoObject(ent, mUtil);

                    if (((IDictionary<string, object>)childEntities).Count() > 0)
                    {
                        foreach (var item in (IDictionary<string, object>)childEntities)
                        {
                            ((IDictionary<string, object>)ent1).Add(item.Key, item.Value);
                        }
                    }
                    childCollection?.Add((ExpandoObject)ent1);
                }
                if ( childCollection.Count == 1 && isArrayRequired == false)
                    ((IDictionary<string, object>)rootExpando).Add(key, childCollection[0]);
                else
                    ((IDictionary<string, object>)rootExpando).Add(key, childCollection);
            }
            return rootExpando;
        }

        /// <summary>
        /// Parses Key attribute collection for alt key support.
        /// </summary>
        /// <param name="keyValues">alt key's for object</param>
        /// <returns>webAPI compliant key string</returns>
        internal static string ParseAltKeyCollection(KeyAttributeCollection keyValues)
        {
            string keycollection = string.Empty;
            foreach (var itm in keyValues)
            {
                if (itm.Value is EntityReference er)
                {
                    keycollection += $"_{itm.Key}_value={er.Id.ToString("P")},";
                }
                else
                {
                    keycollection += $"{itm.Key}='{itm.Value}',";
                }
            }
            return keycollection.Remove(keycollection.Length - 1); // remove trailing ,
        }
        /// <summary>
        /// List of entities to retry retrieves on.
        /// </summary>
        private static List<string> _autoRetryRetrieveEntityList = null;

        /// <summary>
        /// if the Incoming query has an entity on the retry list, returns true.  else returns false.
        /// </summary>
        /// <param name="queryStringToParse">string containing entity name to check against</param>
        /// <returns>true if found, false if not</returns>
        internal static bool ShouldAutoRetryRetrieveByEntityName(string queryStringToParse)
        {
            if (_autoRetryRetrieveEntityList == null)
            {
                _autoRetryRetrieveEntityList = new List<string>();
                _autoRetryRetrieveEntityList.Add("asyncoperation"); // to support failures when looking for async Jobs.
                _autoRetryRetrieveEntityList.Add("importjob"); // to support failures when looking for importjob.
            }

            foreach (var itm in _autoRetryRetrieveEntityList)
            {
                if (queryStringToParse.Contains(itm)) return true;
            }
            return false;
        }

        /// <summary>
        /// Creates or Adds scopes and returns the current scope
        /// </summary>
        /// <param name="scopeToAdd"></param>
        /// <param name="currentScopes"></param>
        /// <returns></returns>
        internal static List<string> AddScope(string scopeToAdd, List<string> currentScopes = null)
        {
            if (currentScopes == null)
                currentScopes = new List<string>();

            if (!currentScopes.Contains(scopeToAdd))
            {
                currentScopes.Add(scopeToAdd);
            }

            return currentScopes;
        }


        /// <summary>
        /// Request Headers used by comms to Dataverse
        /// </summary>
        internal static class RequestHeaders
        {
            /// <summary>
            /// Populated with the host process
            /// </summary>
            public static readonly string USER_AGENT_HTTP_HEADER = "User-Agent";
            /// <summary>
            /// Session ID used to track all operations associated with a given group of calls.
            /// </summary>
            public static readonly string X_MS_CLIENT_SESSION_ID = "x-ms-client-session-id";
            /// <summary>
            /// PerRequest ID used to track a specific request.
            /// </summary>
            public static readonly string X_MS_CLIENT_REQUEST_ID = "x-ms-client-request-id";
            /// <summary>
            /// Content type of WebAPI request.
            /// </summary>
            public static readonly string CONTENT_TYPE = "Content-Type";
            /// <summary>
            /// Header loaded with the AADObjectID of the user to impersonate
            /// </summary>
            public static readonly string AAD_CALLER_OBJECT_ID_HTTP_HEADER = "CallerObjectId";
            /// <summary>
            /// Header loaded with the CRM user ID of the user to impersonate
            /// </summary>
            public static readonly string CALLER_OBJECT_ID_HTTP_HEADER = "MSCRMCallerID";
            /// <summary>
            /// Header used to pass the token for the user
            /// </summary>
            public static readonly string AUTHORIZATION_HEADER = "Authorization";
            /// <summary>
            /// Header requesting the connection be kept alive.
            /// </summary>
            public static readonly string CONNECTION_KEEP_ALIVE = "Keep-Alive";
            /// <summary>
            /// Header requiring Cache Consistency Server side.
            /// </summary>
            public static readonly string FORCE_CONSISTENCY = "Consistency";

            /// <summary>
            /// This key used to indicate if the custom plugins need to be bypassed during the execution of the request.
            /// </summary>
            public const string BYPASSCUSTOMPLUGINEXECUTION = "BypassCustomPluginExecution";

            /// <summary>
            /// key used to apply the operation to a given solution.
            /// See: https://docs.microsoft.com/powerapps/developer/common-data-service/org-service/use-messages#passing-optional-parameters-with-a-request
            /// </summary>
            public const string SOLUTIONUNIQUENAME = "SolutionUniqueName";

            /// <summary>
            /// used to apply duplicate detection behavior to a given request.
            /// See: https://docs.microsoft.com/powerapps/developer/common-data-service/org-service/use-messages#passing-optional-parameters-with-a-request
            /// </summary>
            public const string SUPPRESSDUPLICATEDETECTION = "SuppressDuplicateDetection";

            /// <summary>
            /// used to pass data though Dataverse to a plugin or downstream system on a request.
            /// See: https://docs.microsoft.com/en-us/powerapps/developer/common-data-service/org-service/use-messages#add-a-shared-variable-from-the-organization-service
            /// </summary>
            public const string TAG = "tag";

            /// <summary>
            /// used to identify concurrencybehavior property in an organization request.
            /// </summary>
            public const string CONCURRENCYBEHAVIOR = "ConcurrencyBehavior";

            /// <summary>
            /// Dataverse Platform Property Prefix
            /// </summary>
            public const string DATAVERSEHEADERPROPERTYPREFIX = "MSCRM.";

        }

        /// <summary>
        /// Minim Version numbers for various features of Dataverse API's.
        /// </summary>
        internal static class FeatureVersionMinimums
        {
            /// <summary>
            /// returns true of the feature version is valid for this environment.
            /// </summary>
            /// <param name="instanceVersion">Instance version of the Dataverse Instance</param>
            /// <param name="featureVersion">MinFeatureVersion</param>
            /// <returns></returns>
            internal static bool IsFeatureValidForEnviroment ( Version instanceVersion , Version featureVersion)
            {
                if (instanceVersion != null && (instanceVersion >= featureVersion))
                    return true;
                else
                    return false;
            }

            /// <summary>
            /// Lowest server version that can be connected too.
            /// </summary>
            internal static Version DataverseVersionForThisAPI = new Version("5.0.9688.1533");

            /// <summary>
            /// Minimum version that supports batch Operations.
            /// </summary>
            internal static Version BatchOperations = new Version("5.0.9690.3000");

            /// <summary>
            /// Minimum version that supports holding solutions.
            /// </summary>
            internal static Version ImportHoldingSolution = new Version("7.2.0.9");

            /// <summary>
            /// Minimum version that supports the Internal Upgrade Flag
            /// </summary>
            internal static Version InternalUpgradeSolution = new Version("9.0.0.0");

            /// <summary>
            /// MinVersion that supports AAD Caller ID.
            /// </summary>
            internal static Version AADCallerIDSupported = new Version("8.1.0.0");

            /// <summary>
            /// MinVersion that supports Session ID Telemetry Tracking.
            /// </summary>
            internal static Version SessionTrackingSupported = new Version("9.0.2.0");

            /// <summary>
            /// MinVersion that supports Forcing Cache Sync.
            /// </summary>
            internal static Version ForceConsistencySupported = new Version("9.1.0.0");

            /// <summary>
            /// Minimum version to allow plug in bypass param.
            /// </summary>
            internal static Version AllowBypassCustomPlugin = new Version("9.1.0.20918");

            /// <summary>
            /// Minimum version supported by the Web API
            /// </summary>
            internal static Version WebAPISupported = new Version("8.0.0.0");

            /// <summary>
            /// Minimum version supported for AsyncRibbonProcessing.
            /// </summary>
            internal static Version AllowAsyncRibbonProcessing = new Version("9.1.0.15400");

            /// <summary>
            /// Minimum version supported for Passing Component data to Dataverse as part of solution deployment..
            /// </summary>
            internal static Version AllowComponetInfoProcessing = new Version("9.1.0.16547");

            /// <summary>
            /// Minimum version support for Solution tagging.
            /// </summary>
            internal static Version AllowTemplateSolutionImport = new Version("9.2.21013.00131");

        }


    }
}
