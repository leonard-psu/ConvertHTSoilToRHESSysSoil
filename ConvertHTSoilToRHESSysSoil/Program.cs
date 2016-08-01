using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

//Requires ESRI license
//TODO non esri versions i.e. grass
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Geoprocessor;

namespace ConvertHTSoilToRHESSysSoil
{
    public static class Program
    {
        #region SOIL data structures and functions
        //https://github.com/selimnairb/EcohydroLib/blob/master/ecohydrolib/ssurgo/featurequery.py
        //->getMapunitFeaturesForBoundingBoxTile
        //->  # Get attributes (ksat, texture, %clay, %silt, and %sand) for all components in MUKEYS
        //->  attributes = getParentMatKsatTexturePercentClaySiltSandForComponentsInMUKEYs(mukeys)
        //->  # Compute weighted average of soil properties across all components in each map unit
        //->  avgAttributes = computeWeightedAverageKsatClaySandSilt(attributes)

        //https://github.com/selimnairb/EcohydroLib/blob/master/ecohydrolib/ssurgo/attributequery.py
        //->joinSSURGOAttributesToFeaturesByMUKEY_GeoJSON

        //From http://fiesta.bren.ucsb.edu/~rhessys/appendices/appendix_b_default/appendixb.html#soils
        //From https://github.com/selimnairb/EcohydroLib/blob/master/ecohydrolib/ssurgo/attributequery.py
        public class RHESSys_SoilData
        {
            public string mukey { get; set; }
            public double comppct_r { get; set; }
            public double maxRepComp { get; set; }
            public double hzdept_r { get; set; }
            public double ksat_r { get; set; }
            public double claytotal_r { get; set; }
            public double silttotal_r { get; set; }
            public double sandtotal_r { get; set; }
            public double wsatiated_r { get; set; } //# a.k.a. porosity
            public double wthirdbar_r { get; set; } //# a.k.a. field capacity
            public double awc_r { get; set; } //# a.k.a. plant available water capacity

            public string pmgroupname { get; set; }
            public string texture { get; set; }
            public string texdesc { get; set; }

            public double drnWatCont { get; set; }

        }

        public class SSURGO_Join
        {
            public string mukey { get; set; }
            public double avgKsat { get; set; }
            public double avgClay { get; set; }
            public double avgSilt { get; set; }
            public double avgSand { get; set; }
            public double avgPorosity { get; set; }

        }

        public static double WeightedAverage<T>(this IEnumerable<T> records, Func<T, double> value, Func<T, double> weight)
        {
            double weightedValueSum = records.Sum(x => value(x) * weight(x));
            double weightSum = records.Sum(x => weight(x));

            if (weightSum != 0)
                return weightedValueSum / weightSum;
            else
                throw new DivideByZeroException("WeightedAverage Divide by ZERO!");
        }

        #endregion

        #region ESRI FUNCTIONS
        private static int RunTool(Geoprocessor geoprocessor, IGPProcess process, ITrackCancel TC)
        {

            // Set the overwrite output option to true
            geoprocessor.OverwriteOutput = true;
            //geoprocessor.SetEnvironmentValue("workspace", workspacefolder);
            // Execute the tool            
            try
            {
                geoprocessor.Execute(process, null);
                //ReturnMessages(geoprocessor);

            }
            catch (Exception err)
            {
                Console.WriteLine(err.Message);
                ReturnMessages(geoprocessor);
                return -1;
            }

            return 0;
        }
        private static void ReturnMessages(Geoprocessor gp)
        {
            if (gp.MessageCount > 0)
            {
                for (int Count = 0; Count <= gp.MessageCount - 1; Count++)
                {
                    Console.WriteLine(gp.GetMessage(Count));
                }
            }

        }

        private static LicenseInitializer m_AOLicenseInitializer = new ConvertHTSoilToRHESSysSoil.LicenseInitializer();
        #endregion

        static void Main(string[] args)
        {
            try
            {
                if (args.Count() != 4)
                {
                    Console.WriteLine("Arguments: <gssurgo_xml_input_filename> <gssurgo_shape_filename> <project_name> <output_directory> ");
                    Console.WriteLine("");
                    Console.WriteLine("gssurgo_xml_input_filename: GSSURGO xml file from HydroTerre");
                    Console.WriteLine("gssurgo_shape_filename: GSSURGO shape file from HydroTerre JOINED [i.e. data is joined to this file]");
                    Console.WriteLine("project_name: Base filename for soil tables");
                    Console.WriteLine("output_directory: Directory where soil tables will be created");
                    Console.WriteLine("");
                    return;
                }

                DateTime start_time = DateTime.Now;
                Console.WriteLine("Start Time: " + start_time.ToShortTimeString());

                m_AOLicenseInitializer.InitializeApplication( new esriLicenseProductCode[] { esriLicenseProductCode.esriLicenseProductCodeArcServer }, new esriLicenseExtensionCode[] { });

                String xml_input_filename = args[0]; 
                String ht_soil_shapefile = args[1]; 
                String project_name = args[2]; 
                String output_directory = args[3]; 
                String join_txt_filename_only = project_name + ".txt"; 
                String join_db_filename_only = project_name + ".dbf";

                ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new Geoprocessor();
                gp.OverwriteOutput = true;

                XmlDocument input_xml_filename = new XmlDocument();
                input_xml_filename.Load(xml_input_filename);

                //Assumption: Data Structure is consistent (one huc-12 per forcing file)
                //I.E will not work on HUC-8 scales etc

                //TODO: CHANGE TO NAME BASE, NOT INDEX BASED
                int location_PIHM_GSSURGO_node = 1;
                int location_Soil_Output_node = 2;
                int location_Soil_Count_List_node = 0;
                int location_Soil_List_node = 1;

                string join_field_soil_shapefile = "MUKEY";
                string join_field_soil_table = "mukey";

                XmlNode xml_forcing_node = input_xml_filename.ChildNodes[location_PIHM_GSSURGO_node];
                XmlNode xml_Soil_Output_node = xml_forcing_node.ChildNodes[location_Soil_Output_node];
                XmlNode xml_Soil_Count_List_node = xml_Soil_Output_node.ChildNodes[location_Soil_Count_List_node];
                XmlNode xml_Soil_List_node = xml_Soil_Output_node.ChildNodes[location_Soil_List_node];

                //Using Mukey as key
                Dictionary<string, List<RHESSys_SoilData>> ssurgo_results = new Dictionary<string, List<RHESSys_SoilData>>();
                List<SSURGO_Join> join_data = new List<SSURGO_Join>();

                #region Load GSSURGO Data
                Console.WriteLine("Starting to Load HydroTerre GGSURGO Data");
                foreach (XmlNode soil_record in xml_Soil_List_node.ChildNodes)
                {
                    RHESSys_SoilData result = new RHESSys_SoilData();
                    XmlElement mukey_element = soil_record["MUPOLYGON.MUKEY"];
                    result.mukey = mukey_element.InnerText;

                    XmlElement comppct_r_element = soil_record["component.comppct_r"];
                    result.comppct_r = Convert.ToDouble(comppct_r_element.InnerText);

                    //todo maxRepComp 

                    #region hzdept_r
                    try
                    {
                        XmlElement hzdept_r_element = soil_record["chorizon.hzdept_r"];
                        if (hzdept_r_element == null)
                            result.hzdept_r = -1;
                        else
                            result.hzdept_r = Convert.ToDouble(hzdept_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.hzdept_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to hzdept_r");
                    }
                    #endregion

                    #region ksat_r
                    try
                    {
                        XmlElement ksat_r_element = soil_record["chorizon.ksat_r"];
                        if (ksat_r_element == null)
                            result.ksat_r = -1;
                        else
                            result.ksat_r = Convert.ToDouble(ksat_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.ksat_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to ksat_r");
                    }
                    #endregion

                    #region claytotal_r
                    try
                    {
                        XmlElement claytotal_r_element = soil_record["chorizon.claytotal_r"];
                        if (claytotal_r_element == null)
                            result.claytotal_r = -1;
                        else
                            result.claytotal_r = Convert.ToDouble(claytotal_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.claytotal_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to claytotal_r");
                    }
                    #endregion

                    #region silttotal_r
                    try
                    {
                        XmlElement silttotal_r_element = soil_record["chorizon.silttotal_r"];
                        if (silttotal_r_element == null)
                            result.silttotal_r = -1;
                        else
                            result.silttotal_r = Convert.ToDouble(silttotal_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.silttotal_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to silttotal_r");
                    }
                    #endregion

                    #region sandtotal_r
                    try
                    {
                        XmlElement sandtotal_r_element = soil_record["chorizon.sandtotal_r"];
                        if (sandtotal_r_element == null)
                            result.sandtotal_r = -1;
                        else
                            result.sandtotal_r = Convert.ToDouble(sandtotal_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.sandtotal_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to sandtotal_r");
                    }
                    #endregion

                    #region wsatiated_r
                    try
                    {
                        XmlElement wsatiated_r_element = soil_record["chorizon.wsatiated_r"];
                        if (wsatiated_r_element == null)
                            result.wsatiated_r = -1;
                        else
                            result.wsatiated_r = Convert.ToDouble(wsatiated_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.wsatiated_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to wsatiated_r");
                    }
                    #endregion

                    #region ksat_r
                    try
                    {
                        XmlElement ksat_r_element = soil_record["chorizon.ksat_r"];
                        if (ksat_r_element == null)
                            result.ksat_r = -1;
                        else
                            result.ksat_r = Convert.ToDouble(ksat_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.ksat_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to ksat_r");
                    }
                    #endregion

                    #region claytotal_r
                    try
                    {
                        XmlElement claytotal_r_element = soil_record["chorizon.claytotal_r"];
                        if (claytotal_r_element == null)
                            result.claytotal_r = -1;
                        else
                            result.claytotal_r = Convert.ToDouble(claytotal_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.claytotal_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to claytotal_r");
                    }
                    #endregion

                    #region silttotal_r
                    try
                    {
                        XmlElement silttotal_r_element = soil_record["chorizon.silttotal_r"];
                        if (silttotal_r_element == null)
                            result.silttotal_r = -1;
                        else
                            result.silttotal_r = Convert.ToDouble(silttotal_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.silttotal_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to silttotal_r");
                    }
                    #endregion

                    #region sandtotal_r
                    try
                    {
                        XmlElement sandtotal_r_element = soil_record["chorizon.sandtotal_r"];
                        if (sandtotal_r_element == null)
                            result.sandtotal_r = -1;
                        else
                            result.sandtotal_r = Convert.ToDouble(sandtotal_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.sandtotal_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to sandtotal_r");
                    }
                    #endregion

                    #region wsatiated_r
                    try
                    {
                        XmlElement wsatiated_r_element = soil_record["chorizon.wsatiated_r"];
                        if (wsatiated_r_element == null)
                            result.wsatiated_r = -1;
                        else
                            result.wsatiated_r = Convert.ToDouble(wsatiated_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.wsatiated_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to wsatiated_r");
                    }
                    #endregion

                    #region wthirdbar_r
                    try
                    {
                        XmlElement wthirdbar_r_element = soil_record["chorizon.wthirdbar_r"];
                        if (wthirdbar_r_element == null)
                            result.wthirdbar_r = -1;
                        else
                            result.wthirdbar_r = Convert.ToDouble(wthirdbar_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.wthirdbar_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to wthirdbar_r");
                    }
                    #endregion

                    #region awc_r
                    try
                    {
                        XmlElement awc_r_element = soil_record["chorizon.awc_r"];
                        if (awc_r_element == null)
                            result.awc_r = -1;
                        else
                            result.awc_r = Convert.ToDouble(awc_r_element.InnerText);
                    }
                    catch (Exception ex)
                    {
                        result.awc_r = -1;
                        Console.WriteLine("Warning: Assigning -1 to awc_r");
                    }
                    #endregion

                    #region pmgroupname
                    try
                    {
                        XmlElement pmgroupname_element = soil_record["copmgrp.pmgroupname"];
                        if (pmgroupname_element == null)
                            result.pmgroupname = "Undefined";
                        else
                            result.pmgroupname = pmgroupname_element.InnerText;
                    }
                    catch (Exception ex)
                    {
                        result.pmgroupname = "Undefined";
                        Console.WriteLine("Warning: Assigning Undefined to pmgroupname");
                    }
                    #endregion

                    #region texture
                    try
                    {
                        XmlElement texture_element = soil_record["chtexturegrp.texture"];
                        if (texture_element == null)
                            result.texture = "Undefined";
                        else
                            result.texture = texture_element.InnerText;
                    }
                    catch (Exception ex)
                    {
                        result.texture = "Undefined";
                        Console.WriteLine("Warning: Assigning Undefined to texture");
                    }
                    #endregion

                    #region texdesc
                    try
                    {
                        XmlElement texdesc_element = soil_record["chtexturegrp.texdesc"];
                        if (texdesc_element == null)
                            result.texdesc = "Undefined";
                        else
                            result.texdesc = texdesc_element.InnerText;
                    }
                    catch (Exception ex)
                    {
                        result.texture = "Undefined";
                        Console.WriteLine("Warning: Assigning Undefined to texdesc");
                    }
                    #endregion

                    if (ssurgo_results.ContainsKey(result.mukey))
                    {
                        List<RHESSys_SoilData> soil_list = ssurgo_results[result.mukey];
                        soil_list.Add(result);
                        ssurgo_results[result.mukey] = soil_list;
                    }
                    else
                    {
                        List<RHESSys_SoilData> soil_list = new List<RHESSys_SoilData>();
                        soil_list.Add(result);
                        ssurgo_results.Add(result.mukey, soil_list);
                    }

                }
                Console.WriteLine("Finished Loading Data");
                #endregion

                #region Transform Data for Join
                //Calculate weighted average using component.comppct_r as weights
                foreach (var soil in ssurgo_results.Values)
                {
                    try
                    {
                        bool missing_value_found = false;
                        double ksat = 0;
                        double pctClay = 0;
                        double pctSilt = 0;
                        double pctSand = 0;
                        double porosity = 0;
                        double fieldCap = 0;
                        double avlWatCap = 0;
                        String mukey = "";
                        String pmgroupname = "";
                        String texture = "";
                        String texdesc = "";

                        var ksat_where = soil.Where(b => b.ksat_r != -1);
                        if (ksat_where.Count() > 0)
                        {
                            ksat = ksat_where.WeightedAverage(x => x.ksat_r, x => x.comppct_r);
                        }
                        else
                        {
                            ksat = -9999;
                            missing_value_found = true;
                        }

                        var pctClay_where = soil.Where(b => b.claytotal_r != -1);
                        if (pctClay_where.Count() > 0)
                        {
                            pctClay = pctClay_where.WeightedAverage(x => x.claytotal_r, x => x.comppct_r);
                        }
                        else
                        {
                            pctClay = -9999;
                            missing_value_found = true;
                        }

                        var pctSilt_where = soil.Where(b => b.silttotal_r != -1);
                        if (pctSilt_where.Count() > 0)
                        {
                            pctSilt = pctSilt_where.WeightedAverage(x => x.silttotal_r, x => x.comppct_r);
                        }
                        else
                        {
                            pctSilt = -9999;
                            missing_value_found = true;
                        }

                        var pctSand_where = soil.Where(b => b.sandtotal_r != -1);
                        if (pctSand_where.Count() > 0)
                        {
                            pctSand = pctSand_where.WeightedAverage(x => x.sandtotal_r, x => x.comppct_r);
                        }
                        else
                        {
                            pctSand = -9999;
                            missing_value_found = true;
                        }

                        var porosity_where = soil.Where(b => b.wsatiated_r != -1);
                        if (porosity_where.Count() > 0)
                        {
                            porosity = porosity_where.WeightedAverage(x => x.wsatiated_r, x => x.comppct_r);
                        }
                        else
                        {
                            porosity = -9999;
                            missing_value_found = true;
                        }

                        var fieldCap_where = soil.Where(b => b.wthirdbar_r != -1);
                        if (fieldCap_where.Count() > 0)
                        {
                            fieldCap = fieldCap_where.WeightedAverage(x => x.wthirdbar_r, x => x.comppct_r);
                        }
                        else
                        {
                            fieldCap = -9999;
                            missing_value_found = true;
                        }

                        var avlWatCap_where = soil.Where(b => b.awc_r != -1);
                        if (avlWatCap_where.Count() > 0)
                        {
                            avlWatCap = avlWatCap_where.WeightedAverage(x => x.awc_r, x => x.comppct_r);
                        }
                        else
                        {
                            avlWatCap = -9999;
                            missing_value_found = true;
                        }

                        //What should be done if they are not unique?
                        //How should a null value be treated?
                        var pmgroupname_group = soil.GroupBy(i => i.pmgroupname);
                        if (pmgroupname_group.Count() > 0)
                        {
                            var tmp = pmgroupname_group.Select(group => group.First());
                            pmgroupname = Convert.ToString(tmp.ElementAt(0).pmgroupname);
                        }
                        else
                        {
                            pmgroupname = "UNKNOWN";
                            missing_value_found = true;
                        }

                        var texture_group = soil.GroupBy(i => i.texture);
                        //if (texture_group.Count() < 1)
                        //    Console.WriteLine("Warning: texture_group less than one");
                        //var texture = texture_group.Select(group => group.First());
                        if (texture_group.Count() > 0)
                        {
                            var tmp = texture_group.Select(group => group.First());
                            texture = Convert.ToString(tmp.ElementAt(0).texture);
                        }
                        else
                        {
                            texture = "UNKNOWN";
                            missing_value_found = true;
                        }


                        var texdesc_group = soil.GroupBy(i => i.texdesc);
                        //if (texdesc_group.Count() < 1)
                        //    Console.WriteLine("Warning: texdesc_group less than one");
                        //var texdesc = texdesc_group.Select(group => group.First());
                        if (texdesc_group.Count() > 0)
                        {
                            var tmp = texdesc_group.Select(group => group.First());
                            texdesc = Convert.ToString(tmp.ElementAt(0).texdesc);
                        }
                        else
                        {
                            texdesc = "UNKNOWN";
                            missing_value_found = true;
                        }


                        var mukey_group = soil.GroupBy(i => i.mukey);
                        if (mukey_group.Count() > 0)
                        {
                            var tmp = mukey_group.Select(group => group.First());
                            mukey = Convert.ToString(tmp.ElementAt(0).mukey);
                        }
                        else
                        {
                            mukey = "UNKNOWN";
                            missing_value_found = true;
                        }

                        double drnWatCont = porosity - fieldCap;


                        SSURGO_Join result = new SSURGO_Join();
                        result.mukey = mukey;
                        result.avgKsat = ksat;
                        result.avgClay = pctClay;
                        result.avgSilt = pctSilt;
                        result.avgSand = pctSand;
                        result.avgPorosity = porosity;
                        //result.pmgroupname = pmgroupname;
                        //result.avgSand = texture;
                        //result.avgSand = texdesc;
                        //result.avgSand = fieldCap;
                        //result.avgSand = avlWatCap;

                        join_data.Add(result);

                        if (missing_value_found)
                        {
                            Console.WriteLine("Warning: Invalid values found.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }

                #endregion

                #region Write Soil Outputs
                String header = "mukey,avgKsat,avgClay,avgSilt,avgSand,avgPorosity";

                using (System.IO.StreamWriter file = new System.IO.StreamWriter(output_directory + "\\" + join_txt_filename_only))
                {
                    file.WriteLine(header);
                    foreach (var data in join_data)
                    {
                        file.WriteLine(data.mukey + "," + data.avgKsat + "," + data.avgClay + "," + data.avgSilt + "," + data.avgSand + "," + data.avgPorosity);

                    }
                }
                #endregion

                #region Convert text file to esri table for joining
                
                ESRI.ArcGIS.ConversionTools.TableToTable table_conversion = new ESRI.ArcGIS.ConversionTools.TableToTable();
                table_conversion.in_rows = join_txt_filename_only;
                table_conversion.out_path = output_directory;
                table_conversion.out_name = join_db_filename_only;
                RunTool(gp, table_conversion, null);

                #endregion

                #region Join esri table to soil shapefile

                ESRI.ArcGIS.DataManagementTools.JoinField join_soil_table = new ESRI.ArcGIS.DataManagementTools.JoinField();
                join_soil_table.in_data = ht_soil_shapefile;
                join_soil_table.in_field = join_field_soil_shapefile;
                join_soil_table.join_table = output_directory + "//" + join_db_filename_only;
                join_soil_table.join_field = join_field_soil_table;
                join_soil_table.fields = "";
                RunTool(gp, join_soil_table, null);

                #endregion
                gp = null;

                m_AOLicenseInitializer.ShutdownApplication();

                Console.WriteLine("Finished");
                DateTime end_time = DateTime.Now;
                TimeSpan ts = end_time - start_time;
                Console.WriteLine("End Time: " + end_time.ToShortTimeString());
                Console.WriteLine("Took: " + ts.TotalSeconds + " seconds");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
