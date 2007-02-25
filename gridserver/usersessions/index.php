<?
// This file parses URLs of the format:
// usersessions/key/userid/data
// where key is the key to authenticate with the grid, userid is the user's LLUUID and data is the data about the user's session being requested
// if the data requested is left out, an XML response will be sent

error_reporting(E_ALL); // Remember kids, PHP errors kill XML-RPC responses and REST too! will the slaughter ever end?

include("../gridserver_config.inc.php");
include("../../common/database.inc.php");
include("../../common/util.inc.php");

// Parse out the parameters from the URL
$params = str_replace($grid_home,'', $_SERVER['REQUEST_URI']);
$params = str_replace("index.php/","",$params);
$params = split('/',$params);

// Die if the key doesn't match
if($params[1]!=$sim_recvkey) {
    die();
}

$link = mysql_connect($dbhost,$dbuser,$dbpasswd)
 OR die("Unable to connect to database");

mysql_select_db($dbname)
 or die("Unable to select database");

$agent_id = strtolower($params[2]);
$query = "SELECT * FROM sessions WHERE agent_id='$agent_id' AND session_active=1";

$result = mysql_query($query);
if(mysql_num_rows($result)>0) {
	$info=mysql_fetch_assoc($result);
        $circuit_code = $info['circuit_code'];
        $secure_session_id=$info['secure_session_id'];
        $session_id=$info['session_id'];

        $query = "SELECT * FROM local_user_profiles WHERE userprofile_LLUUID='$agent_id'";
        $result=mysql_query($query);
        $userinfo=mysql_fetch_assoc($result);
        $firstname=$userinfo['profile_firstname'];
        $lastname=$userinfo['profile_lastname'];
        $agent_id=$userinfo['userprofile_LLUUID'];
}

// if only 4 params, assume we are sending an XML response
if(count($params)==3) {
	output_xml_block("usersession",Array(
            'authkey' => $sim_sendkey,
            'circuit_code' => $circuit_code,
            'agent_id' => $agent_id,
            'session_id' => $session_id,
            'secure_session_id' => $secure_session_id,
            'firstname' => $firstname,
            'lastname' => $lastname
        ));
}
}

?>
